open System
open Server
open FsWorksheet
open FsWatch
open FSharp.Control.Reactive
open FSharp.Compiler

[<EntryPoint>]
let main argv =
    let name, file = argv.[0], argv.[1]
    use client = Rpc.createClient name
    use subject = Subject.broadcast

    let config post = { 
        filename = file
        onBeforeEvaluation = fun cell -> 
            post <| PreEval (Range.toVsRange cell.range, cell.eqHash) 
        onAfterEvaluation = fun cell -> 
            post <| PostEval (Range.toVsRange cell.range, cell.eqHash, cell.runs)
        onStateChanged = fun (_, state) -> 
            post <| CellsChanged (
                state.cells 
                |> Seq.map(fun cell -> { hash = cell.eqHash; range = Range.toVsRange cell.range })
                |> Seq.toArray
            )
            
    }

    let post message = Async.Start <| async { 
        let! response = client.post message 
        return ()
    }
    
    use watch = FsWatch.watch (config post, subject)

    let rec loop () = async {
        let! reply = client.post Query
        let command = defaultArg reply Noop
        
        match command with
        | Noop -> 
            do! Async.Sleep 100
            return! loop ()
        | Exit -> 
            return ()
        | command ->
            subject.OnNext command
            return! loop ()
    }

    printfn "Server started for %s" file
    loop () |> Async.RunSynchronously
    0 // return an integer exit code
