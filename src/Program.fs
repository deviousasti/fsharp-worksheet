
open System
open System.IO
open System.Text
open FSharp.Compiler.Interactive.Shell
open System.Diagnostics
open FSharp.Compiler
open FSharp.Compiler.SourceCodeServices
open FSharp.Compiler.SyntaxTree
open System.Threading
open Microsoft.FSharp.Control
open FSharp.Control.Reactive

open FSharp.Compiler.Range
open System.Collections.Generic
open Worksheet.Core
open RangeTree

type range = Range.range

#pragma "worksheet"

//#r "nuget: FSharp.Compiler.Service"
// Initialize output and input streams

module AstTraversal = 
    
    let rec unwrapModule moduleIdent decls = seq {
        for decl in decls do
            match decl with 
            | SynModuleDecl.NestedModule(isRecursive = isRecursive; decls = decls; moduleInfo = ComponentInfo(longId = ident)) ->                
                yield! unwrapModule (moduleIdent @ ident) decls
            | decl -> yield moduleIdent, decl
    }        

    let unwrapNamespace modules = seq {
        for synmod in modules do
            let (SynModuleOrNamespace(decls = decls; longId = ident; range = range)) = synmod
            // implicit top-level module
            yield! unwrapModule [] decls
    }

    let declarationsFor (file: FSharpParseFileResults) =         
        match file.ParseTree with
            | (Some (ParsedInput.ImplFile(implFile))) ->
                let (ParsedImplFileInput(fn, script, name, pragmas, directives, modules, _)) = implFile
                unwrapNamespace modules
            | _ -> failwith "F# Interface file (*.fsi) not supported."
    
    let isLeadingWhiteSpace (source: FSharp.Compiler.Text.ISourceText) (range: range) =
        source.GetLineString(range.StartLine - 1).AsSpan(0, range.StartColumn).IsWhiteSpace()

    let rangeToText (source: FSharp.Compiler.Text.ISourceText) (range: range) = 
        if range.StartLine = range.EndLine then
            if isLeadingWhiteSpace source range then
                source.GetLineString(range.StartLine - 1)
            else
                source.GetLineString(range.StartLine - 1).Substring(range.StartColumn, range.EndColumn - range.StartColumn)
        else
            let sb = new StringBuilder()            

            if isLeadingWhiteSpace source range then
                sb.Append(source.GetLineString(range.StartLine - 1)).AppendLine() |> ignore
            else
                sb.Append(source.GetLineString(range.StartLine - 1).AsSpan(range.StartColumn)).AppendLine() |> ignore
            
            for i in range.StartLine..(range.EndLine - 2) do
                sb.AppendLine(source.GetLineString(i)) |> ignore
           
            sb.Append(source.GetLineString(range.EndLine - 1).AsSpan(0, range.EndColumn)).AppendLine() |> ignore
           
            sb.ToString()
    
    let astToText (source: FSharp.Compiler.Text.ISourceText) (decl: SynModuleDecl) (scope: LongIdent) =
        match decl with
        | SynModuleDecl.Open (ident, range) -> sprintf "open %s" (rangeToText source range)
        | SynModuleDecl.HashDirective(ParsedHashDirective("pragma", ["worksheet"], _), _) ->      
            //ignore our own pragma directive
            ""
        | SynModuleDecl.HashDirective(ParsedHashDirective("load", [filename], _), range) ->           
            // retarget script path
            let root = Path.GetDirectoryName range.FileName
            let fullPath = if Path.IsPathRooted filename then filename else Path.Combine (root, filename)
            sprintf "#load \"%s\"" fullPath
        | decl -> 
            let text = rangeToText source decl.Range
            (scope 
            |> Seq.mapi(fun i id -> sprintf "%smodule %s = \n" (String.replicate i "    ") id.idText) 
            |> String.concat "")                
                + text
            
        

module Range =
    let intersects (a : range) (b: range) =
        (a.StartLine < b.EndLine) &&
        (b.StartLine < a.EndLine)
    
    let isAfter (a : range) (b: range) =
        b.StartLine > a.StartLine && b.EndLine > a.EndLine

    let rangeFor file (original : Range.range) =
        #if DEBUG
        Range.mkRange file (original.Start) (original.End)
        #else
        original
        #endif    


module Seq = 
    
    let toDict keySelector enumerable = 
        let map = new Dictionary<_, _>()
        for item in enumerable do
            map.[keySelector item] <- item
        map
    
    let toLookup keySelector enumerable = 
        System.Linq.Enumerable.ToLookup(enumerable, keySelector)

    let toHashSet enumerable = 
        let set = new HashSet<_>()
        for item in enumerable do
            if not (set.Add item) then do
                set.Remove item |> ignore
                set.Add item |> ignore
        set

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
    } with
        member this.range = this.ast.Range
        member this.ToSource(source) =
            AstTraversal.astToText source this.ast this.scope

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
    type SymbolLookup = Linq.ILookup<FSharpSymbol, FSharpSymbolUse>
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
        new Worksheet.Core.EvalContext (filename = filename)

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

                match this with
                | NonEval | Unevaluated -> [||]
                | Evaled runs | Faulted runs -> runs

        RunWriter.PrintToConsole (cell.result.Runs)

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

module Disposable =
    let create action = { new IDisposable with member _.Dispose() = action () }
    let combine (disp1 : #IDisposable) (disp2 : #IDisposable) =
        create (fun () -> disp1.Dispose(); disp2.Dispose())

module FsWatch = 
    type FileSystemWatcher with
        member this.Replaced = 
            this.Renamed |> Observable.map (fun e -> new FileSystemEventArgs(e.ChangeType, Path.GetDirectoryName e.FullPath, e.Name))
    
    let private watchForChanges file callback = 
        let root = Path.GetDirectoryName (file : string)
        let fswatcher = new FileSystemWatcher(root, "*.*", IncludeSubdirectories = false)        
        let subscription = 
            Observable.merge fswatcher.Changed fswatcher.Created  
            |> Observable.merge fswatcher.Replaced
            |> Observable.filter (fun e -> e.FullPath = file) 
            |> Observable.throttle (TimeSpan.FromMilliseconds 100.0)
            |> Observable.subscribe (fun e -> callback ())
        
        // start watch
        fswatcher.EnableRaisingEvents <- true
        Disposable.combine subscription fswatcher    

    let watchFile file onEvaluation =
        let ctx = Worksheet.createContext file
        let initstate = { Worksheet.initState with onEvaluation = onEvaluation }
        let state = ref initstate            
        let rec compute () = Async.RunSynchronously <| async {
            try
                let! next = Worksheet.evalFile file !state ctx
                state := next
            with
            | _ -> compute ()
        }
        compute ()
        let subscription = watchForChanges file compute       
        Disposable.combine ctx subscription
    

[<EntryPoint>]
let main argv =
    let file = Array.head argv
    use diposable = FsWatch.watchFile file Worksheet.printCellToConsole 
    Console.ReadLine() |> ignore
    0