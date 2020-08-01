namespace FsWorksheet

open System
open System.IO
open FSharp.Control.Reactive
open FSharp.Compiler
open Worksheet

module FsWatch = 

    type WorksheetCommand =
    | Compute of source: string
    | ForceEvalCellAt of line : int * col : int
    | ForceEvalRange of startline : int * startcol : int * endline : int * endcol : int
    | Interrupt
    | Exit

    type WatchFileConfig =
        { filename: string
          onBeforeEvaluation: Worksheet.EvalCallback
          onAfterEvaluation: Worksheet.EvalCallback
        }

    [<CompiledName("Eval")>]
    let eval command state ctx = 
        match command with 
        | Compute source -> 
            Worksheet.evalSource source state ctx
        | ForceEvalCellAt (line, col) -> 
            let pos = Range.mkPos line col
            Worksheet.forceCellAt pos state ctx
        | ForceEvalRange (startline, startcol, endline, endcol) -> 
            let range = Range.mkRange "" (Range.mkPos startcol startline) (Range.mkPos endcol endline)
            Worksheet.forceAllCellsAt range state ctx
        | Interrupt ->
            ctx.Interrupt()
            async { return state }
        | Exit -> 
            (ctx :> IDisposable).Dispose()
            exit 0
        
    [<CompiledName("Watch")>]
    let watch (config: WatchFileConfig, observable) =
        let ctx = Worksheet.createContext ()
        let initstate = { 
            Worksheet.initState with 
                onBeforeEvaluation = config.onBeforeEvaluation
                onAfterEvaluation = config.onAfterEvaluation
        }
        let state = ref initstate            
        let compute command = Observable.ofAsync <| async {            
            let current = !state
            let! next = eval command current ctx
            state := next
        }

        let subscription = 
            observable
            |> Observable.map compute
            |> Observable.switch
            |> Observable.retry
            |> Observable.subscribe ignore

        Disposable.compose ctx subscription

    
    type FileSystemWatcher with
        /// A file being replaced 
        /// When editing files in Visual Studio, 
        /// it creates temporary files then deletes the original file 
        /// and renames the temporary file to the same name. 
        /// While other application edits will trigger the System.IO.FileSystemWatcher's Changed event, 
        /// for edits in Visual Studio, we need to handle the Renamed event.
        member this.Replaced = 
            this.Renamed 
            |> Observable.map (fun e -> 
                new FileSystemEventArgs(e.ChangeType, Path.GetDirectoryName e.FullPath, e.Name))    
       
    [<CompiledName("ObserveChanges")>]
    let watchForChanges (file : string) = 
        let root = Path.GetDirectoryName (file : string)        
        if not (File.Exists file) then
            failwithf "'%s' is an invalid filename" file

        Observable.using 
            (fun () -> new FileSystemWatcher(root, "*.*", IncludeSubdirectories = false))        
            (fun fswatcher -> 
                // start watch
                fswatcher.EnableRaisingEvents <- true

                Observable.merge fswatcher.Changed fswatcher.Created  
                |> Observable.merge fswatcher.Replaced
                |> Observable.filter (fun e -> e.FullPath = file) 
                |> Observable.throttle (TimeSpan.FromMilliseconds 100.0)
                |> Observable.map (fun _ -> ())        
            )        
    

    [<CompiledName("WatchFile")>]
    let watchFile (config: WatchFileConfig) =
        let sourceChanges = 
            watchForChanges config.filename
            |> Observable.startWith [()]
            |> Observable.flatmapTask (fun () -> File.ReadAllTextAsync(config.filename))
            |> Observable.map Compute
        
        watch (config, sourceChanges)
    



