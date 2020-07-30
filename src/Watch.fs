namespace FsWorksheet

open System
open System.IO
open Microsoft.FSharp.Control
open FSharp.Control.Reactive


module FsWatch = 
    
    type FileSystemWatcher with
        member this.Replaced = 
            this.Renamed 
            |> Observable.map (fun e -> 
                new FileSystemEventArgs(e.ChangeType, Path.GetDirectoryName e.FullPath, e.Name))
    
    let private watchForChanges file = 
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

    let watchFile file onEvaluation =
        let ctx = Worksheet.createContext file
        let initstate = { Worksheet.initState with onEvaluation = onEvaluation }
        let state = ref initstate            
        let compute () = Observable.ofAsync <| async {            
            let! next = Worksheet.evalFile file !state ctx
            state := next
        }

        let subscription = 
            watchForChanges file     
            |> Observable.startWith [()]
            |> Observable.map compute
            |> Observable.switch
            |> Observable.retry
            |> Observable.subscribe ignore

        Disposable.compose ctx subscription
    



