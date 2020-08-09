open System
open Remoting
open FsWorksheet
open FsWorksheet.API
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
        events = {
            onBeforeEval = fun cell -> 
                post <| PreEval ({ range = Range.toVsRange cell.range; eqHash = cell.eqHash }, cell.ToSource()) 
            onAfterEval = fun cell -> 
                post <| PostEval ({ range = Range.toVsRange cell.range; eqHash = cell.eqHash }, cell.runs)
            onStaging = fun (_, state) -> 
                post <| ChangesStaged (
                    state.cells 
                    |> Seq.map(fun cell -> { eqHash = cell.eqHash; range = Range.toVsRange cell.range })
                    |> Seq.toArray
                )
            onCommit = ignore   
        }
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
