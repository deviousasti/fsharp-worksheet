open System
open Remoting
open FsWorksheet
open FsWorksheet.API
open FsWatch
open FSharp.Control.Reactive
open FSharp.Compiler
open System.Threading

[<EntryPoint>]
let main argv =
    let name, file = argv.[0], argv.[1]
    use client = Rpc.createClient name 200
    use subject = Subject.broadcast

    let sendReceive message = 
        lock client <| fun () -> 
        Async.RunSynchronously <| async { 
        return! client.post message 
    }

    let send = sendReceive >> ignore

    let config = { 
        filename = file
        events = {
            onBeforeEval = fun cell -> 
                send <| PreEval ({ range = Range.toVsRange cell.range; eqHash = cell.eqHash }, cell.ToSource()) 
            onAfterEval = fun cell -> 
                send <| PostEval ({ range = Range.toVsRange cell.range; eqHash = cell.eqHash }, cell.runs)
            onStaging = fun (state) -> 
                send <| ChangesStaged (
                    state.cells 
                    |> Seq.map(fun cell -> { eqHash = cell.eqHash; range = Range.toVsRange cell.range })
                    |> Seq.toArray
                )
            onCommit = fun _ ->
                send <| Committed
        }
    }

    use watch = FsWatch.watch (config, subject)
    printfn "Server started for %s" file

    while true do
        let reply = sendReceive Query            
        let command = defaultArg reply Timedout
        
        match command with
        | Timedout ->
            ()
        | Noop | Ack -> 
            Thread.Sleep 100
            ()
        | Exit -> 
            exit 0
        | command ->
            printfn "Recv"
            subject.OnNext command    

    0 // return an integer exit code
