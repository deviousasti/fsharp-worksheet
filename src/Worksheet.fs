namespace FsWorksheet

open System
open System.IO
open FSharp.Compiler
open FSharp.Compiler.SourceCodeServices
open FSharp.Compiler.SyntaxTree
open Microsoft.FSharp.Control
open FSharp.Control.Reactive

open FSharp.Compiler.Range
open FsWorksheet.Core
open IntervalTree


module Worksheet = 

    type Source = Text.ISourceText
    let createSource text = Text.SourceText.ofString text

    type Runs = Run[]
    type RunResult = NonEval | Evaled of Runs | Faulted of Runs * Runs | Unevaluated    
    
    [<RequireQualifiedAccess>]
    type 'a Dependencies = NonCalculated | Nothing | DependsOn of 'a list

    [<CustomEquality; CustomComparison>]
    type Cell = { 
        scope: LongIdent;
        ast: SynModuleDecl; 
        result: RunResult; 
        eqHash: int32; 
        session: int32;
        source: Source;
    } with
        member this.range = this.ast.Range
        member this.ToSource(source) =
            AstTraversal.astToText source this.ast this.scope
        member this.ToSource() = this.ToSource(this.source)           
        member this.runs = 
            match this.result with
            | Evaled runs -> runs :> Run seq 
            | Faulted (runs, errs) -> Seq.append runs errs
            | _ -> Seq.empty
        override this.GetHashCode() = this.eqHash
        override this.Equals(other) = 
            match other with
            | :? Cell as cell -> cell.eqHash = this.eqHash
            | _ -> false
        interface IEquatable<Cell> with
            member this.Equals(cell) = this.eqHash = cell.eqHash
        interface IComparable<Cell> with
            member this.CompareTo(cell) = this.eqHash.CompareTo(cell.eqHash)
    
    // mutable hashset is 2x faster than immutable set
    // we don't actually use the mutable behavior of hashset
    type Cells = System.Collections.Generic.HashSet<Cell>
    type State =
        { source: Source
          cells: Cells
          session: int32
        }


    type Events = {
        onAfterEval: Cell -> unit
        onBeforeEval: Cell -> unit
        onStaging: State -> unit
        onCommit: (State * State) -> unit;
    } with  
        static member none = { 
            onAfterEval = ignore
            onBeforeEval = ignore
            onStaging = ignore
            onCommit = ignore 
        }
    
    type Context = { ctx: EvalContext; events: Events } with
        interface IDisposable with
            member this.Dispose() = Disposable.dispose this.ctx

    type SymbolRangeTree = IIntervalTree<pos, FSharpSymbolUse>

    type CheckedSource = { 
        parseResults : FSharpParseFileResults 
        checkResults: FSharpCheckFileResults 
        source: Source
        allSymbols: FSharpSymbolUse[]
        symbols: SymbolRangeTree
    }
    
    let isValidSymbolUse (usage: FSharpSymbolUse) =
        usage.Symbol.Assembly.QualifiedName = "" &&
        not usage.IsFromDefinition &&
        not usage.IsFromType

    let checkSource text filename ({ ctx = ctx}) = async {
        let source = createSource text
        let! (parseResults, checkResults) = 
            match filename with
            | None -> ctx.Check text
            | Some filename -> ctx.Check text
        
        if parseResults.ParseHadErrors then
            return None
        else
            let! symbols = checkResults.GetAllUsesOfAllSymbolsInFile()        
            let ranges = new IntervalTree.IntervalTree<_, _>(Range.posOrder)        
            for usage in symbols do
                if isValidSymbolUse usage then
                    let loc = usage.RangeAlternate
                    ranges.Add (loc.Start, loc.End, usage)

            //let lookup = Linq.Enumerable.ToLookup(symbols, fun sym -> sym.Symbol)   
        
            return Some <| { 
                parseResults = parseResults 
                checkResults = checkResults 
                source = source
                symbols = ranges
                allSymbols = symbols
            }                              
    }

    let createContext () =
        new FsWorksheet.Core.EvalContext ()

    let initState = { source = createSource ""; cells = new Cells(); session = 0; }
    
    // use spans to avoid any allocation
    // https://stackoverflow.com/questions/51864673/c-sharp-readonlyspanchar-vs-substring-for-string-dissection

    let inline private hashSpan span = String.GetHashCode span
    let private addHash span (hash : byref<HashCode>) = 
        let hashcode = hashSpan span
        if not (span.IsEmpty && span.IsWhiteSpace()) then do
            hash.Add hashcode

    let computeHash (source: Source) (scope: LongIdent, ast: SynModuleDecl) =
        let range = ast.Range
        let mutable hash = HashCode()           

        // written imperatively for speed
        // changes are usually expected to be minimal and tail-facing

        for ident in scope do
            addHash (ident.idText.AsSpan()) &hash
        
        if range.StartLine = range.EndLine then
            addHash (source.GetLineString(range.StartLine - 1).AsSpan(range.StartColumn, range.EndColumn - range.StartColumn)) &hash
        else
            addHash (source.GetLineString(range.StartLine - 1).AsSpan(range.StartColumn)) &hash

            for i in range.StartLine..(range.EndLine - 2) do
                addHash (source.GetLineString(i).AsSpan()) &hash
            
            addHash (source.GetLineString(range.EndLine - 1).AsSpan(0, range.EndColumn)) &hash 
            
        hash.ToHashCode()
    
    let computeDiff (old: State) (cur: CheckedSource) = 
        let source, res = cur.source, cur.parseResults
        let decls = AstTraversal.declarationsFor res
        let session = old.session + 1
        // if you have the exact same source code at two
        // different locations in the code, shadow the former definition
        
        let changes = seq {
            for loc in decls do            
                let (ident, ast) = loc
                let newcell = { 
                    ast = ast
                    scope = ident 
                    result = NonEval 
                    eqHash = computeHash source loc
                    session = session
                    source = source
                }

                match old.cells.TryGetValue newcell with
                | true, oldcell -> 
                    // same hash, but different locations
                    { newcell with result = oldcell.result; }
                | false, _ -> newcell
        }
        
        // compute forward dependency DAG
        // this is linear O(n), and in general 
        // faster than computing the dependents of each symbols
        // and doing a distinctBy
        let cells = seq {
            let depTree = new IntervalTree<_, _>(Range.posOrder)

            for cell in changes do                
                let symbols = cur.symbols.Query(cell.range.Start, cell.range.End)
                let isAffected =
                    symbols 
                    |> Seq.exists (fun sym -> 
                        match sym.Symbol.DeclarationLocation with
                        | None -> false
                        | Some loc -> 
                            let dependencies = depTree.Query(loc.Start, loc.End)
                            not (Seq.isEmpty dependencies)
                    )
                
                let cell = if isAffected then { cell with result = NonEval; session = session } else cell

                if cell.result = NonEval then do
                    let range = cell.range
                    depTree.Add (range.Start, range.End, cell)                

                yield cell

        }
        
        { old with cells = Cells cells; source = cur.source; session = session }

    let evalCells (state: State) ({ ctx = ctx; events = events }) = seq {         
        for cell in state.cells ->
            if cell.result = NonEval then
                    async {
                    do! ctx.ReleaseStreams()

                    let src = cell.ToSource()
                    events.onBeforeEval cell
                    
                    let! result = ctx.Eval(src)

                    let runResult = 
                        match result with
                        | Ok (runs, _) -> Evaled runs
                        | Error err -> Faulted err

                    let cell = { cell with result = runResult } 

                    events.onAfterEval cell
                    return cell
                }
            else 
                async.Return cell
    }

    let evalState (state: State) (ctx: Context) = async {
        do ctx.events.onStaging state
        let! cells = evalCells state ctx |> Async.Sequential
        let next = { state with cells = Cells cells }
        do ctx.events.onCommit (state, next)
        return next
    }

    let forceCells (state: State) condition = 
        let newcells = 
            seq { 
                for cell in state.cells do
                    if condition cell then
                        { cell with result = NonEval }
                    else
                        cell
            }

        evalState { state with cells = Cells newcells }     

    let forceCellAt pos (state: State) = 
        forceCells state (fun cell -> Range.rangeContainsPos cell.range pos)

    let forceAllCellsAt range (state: State) = 
        forceCells state (fun cell -> Range.intersects cell.range range)

    let evalSource source (state: State) (ctx: Context) = async {
        match! checkSource source None ctx with
        | None -> 
            return state
        | Some checkedSource -> 
            let diff = computeDiff state checkedSource        
            return! evalState diff ctx
    }

    let evalFile file (state: State) (ctx: Context) = async {
        let! token = Async.CancellationToken
        let! source = File.ReadAllTextAsync (file, token) |> Async.AwaitTask
        return! evalSource source state ctx
    }

module Print = 
    
    open Worksheet

    let resultToConsole (cell : Cell) =
        RunWriter.PrintToConsole cell.runs

    let sourceToConsole (cell : Cell) =
        Console.ForegroundColor <- ConsoleColor.Gray
        printfn "%s" (String.replicate Console.WindowWidth "─")
        Console.ForegroundColor <- ConsoleColor.DarkGray
        printfn "%s" (cell.ToSource())
        Console.ResetColor()

    let cellToConsole cell =        
        sourceToConsole cell
        resultToConsole cell
