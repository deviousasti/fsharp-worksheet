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
    use client = JsonRpc.createClient name
    use subject = Subject.broadcast

    let send = client.send >> ignore

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

    let rec loop () = async {
        let! command = client.sendReceive Query    
        match command with
        | Noop | Ack -> 
            do! Async.Sleep 250
            return! loop ()
        | Exit -> 
            exit 0
        | command ->
            printfn "Process"
            subject.OnNext command  
            return! loop ()
    }    
    
    try 
        loop () |> Async.RunSynchronously
        0 // return proper exit
    with
    | _ -> 
        printfn "Disconnected"
        -1 // disconnected
    
