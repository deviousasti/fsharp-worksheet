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
    use client = Rpc.createClient name 2000
    use subject = Subject.broadcast

    let post message = 
        lock client <| fun () -> 
        Async.RunSynchronously <| async { 
        let! _ = client.post message 
        return ()
    }

    let config = { 
        filename = file
        events = {
            onBeforeEval = fun cell -> 
                post <| PreEval ({ range = Range.toVsRange cell.range; eqHash = cell.eqHash }, cell.ToSource()) 
            onAfterEval = fun cell -> 
                post <| PostEval ({ range = Range.toVsRange cell.range; eqHash = cell.eqHash }, cell.runs)
            onStaging = fun (state) -> 
                post <| ChangesStaged (
                    state.cells 
                    |> Seq.map(fun cell -> { eqHash = cell.eqHash; range = Range.toVsRange cell.range })
                    |> Seq.toArray
                )
            onCommit = fun _ ->
                post <| Committed
        }
    }

    use watch = FsWatch.watch (config, subject)

    let rec loop next = async {
        let! reply = client.post next
        let command = defaultArg reply Timedout
        
        match command with
        | Timedout ->
            return! loop Query
        | Noop | Ack -> 
            do! Async.Sleep 100
            return! loop Query
        | Exit -> 
            return ()
        | command ->
            printfn "Recv"
            subject.OnNext command
            return! loop Query
    }

    printfn "Server started for %s" file
    loop Query |> Async.RunSynchronously
    0 // return an integer exit code
