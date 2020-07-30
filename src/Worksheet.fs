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
open RangeTree



module Worksheet = 

    type Source = Text.ISourceText
    let createSource text = Text.SourceText.ofString text

    type Runs = Run[]
    type RunResult = NonEval | Evaled of Runs | Faulted of Runs | Unevaluated    
    
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

        override this.GetHashCode() = this.eqHash
        override this.Equals(other) = 
            match other with
            | :? Cell as cell -> cell.eqHash = this.eqHash
            | _ -> false
        interface IEquatable<Cell> with
            member this.Equals(cell) = this.eqHash = cell.eqHash
        interface IComparable<Cell> with
            member this.CompareTo(cell) = this.eqHash.CompareTo(cell.eqHash)

    type Cells = System.Collections.Generic.HashSet<Cell>
    type State = { source: Source; cells: Cells; session: int32; onEvaluation: (string * Cell) -> unit }    
    type SymbolRangeTree = IRangeTree<pos, FSharpSymbolUse>

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

    let checkSource text filename (ctx: EvalContext) = async {
        let source = createSource text
        let! (parseResults, checkResults) = 
            match filename with
            | None -> ctx.Check text
            | Some filename -> ctx.Check (source, filename)
 
        let! symbols = checkResults.GetAllUsesOfAllSymbolsInFile()        
        let ranges = new RangeTree.RangeTree<_, _>(Range.posOrder)        
        for usage in symbols do
            if isValidSymbolUse usage then
                let loc = usage.RangeAlternate
                ranges.Add (loc.Start, loc.End, usage)

        //let lookup = Linq.Enumerable.ToLookup(symbols, fun sym -> sym.Symbol)   
        
        return { 
            parseResults = parseResults 
            checkResults = checkResults 
            source = source
            symbols = ranges
            allSymbols = symbols
        }                              
    }

    let createContext filename =
        new FsWorksheet.Core.EvalContext (filename = filename)

    let initState = { source = createSource ""; cells = new Cells(); session = 0; onEvaluation = ignore }
    
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
    
    let printCellToConsole (src, cell : Cell) =
        Console.ForegroundColor <- ConsoleColor.Gray
        printfn "%s" (String.replicate Console.WindowWidth "─")
        Console.ForegroundColor <- ConsoleColor.DarkGray
        printfn "%s" src 
        let runs =
            match cell.result with
            | Evaled runs 
            | Faulted runs -> runs
            | _ -> [||]

        RunWriter.PrintToConsole runs

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
        let cells = seq {
            let depTree = new RangeTree<_, _>(Range.posOrder)

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

    let evalCells (state: State) (ctx: EvalContext) = seq {         
        for cell in state.cells ->
            if cell.result = NonEval then
                    async {
                    let src = cell.ToSource(state.source)
                    let! result = ctx.Eval(src)

                    let runResult = 
                        match result with
                        | Ok (runs, _value) -> Evaled runs
                        | Error (runs, [||]) -> Faulted runs
                        | Error (_, errs) -> Faulted errs

                    let cell = { cell with result = runResult } 

                    state.onEvaluation (src, cell)
                    return cell
                }
            else async { 
                    return cell
            }
    }

    let evalState (state: State) (ctx: EvalContext) = async {
        let! cells = evalCells state ctx |> Async.Sequential
        return { state with cells = Cells cells }
    }

    let evalFile file (state: State) (ctx: EvalContext) = async {
        let! token = Async.CancellationToken
        let! source = File.ReadAllTextAsync (file, token) |> Async.AwaitTask
        let! checkedSource = checkSource source None ctx
        let diff = computeDiff state checkedSource
        return! evalState diff ctx
    }


module Print = 
    
    open Worksheet

    let resultToConsole (cell : Cell) =
        let runs =
            match cell.result with
            | Evaled runs 
            | Faulted runs -> runs
            | _ -> [||]

        RunWriter.PrintToConsole runs

    let sourceToConsole (cell : Cell) =
        Console.ForegroundColor <- ConsoleColor.Gray
        printfn "%s" (String.replicate Console.WindowWidth "─")
        Console.ForegroundColor <- ConsoleColor.DarkGray
        printfn "%s" (cell.ToSource())

    let cellToConsole cell =        
        sourceToConsole cell
        resultToConsole cell
