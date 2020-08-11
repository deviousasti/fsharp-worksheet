namespace FsWorksheet

open System
open System.Collections.Concurrent
open Remoting
open System.Threading

type ChangeEvents =
| Unchanged     = 0 
| Added         = 1
| Moved         = 2
| Removed       = 3
| Evaluating    = 4
| Evaluated     = 5
| Committed     = 6

type WorksheetModel (name, file, threadModel: Action<Async<unit>>) =
    let queue = new ConcurrentQueue<_>()
    let state = ref (vscells ())
    let events = new Event<_>()
    let raiseWith (kind : ChangeEvents) (cell : vscell) runs = 
        //lock events (fun () -> events.Trigger struct (kind, cell, runs))
        threadModel.Invoke <| async { 
            do events.Trigger struct (kind, cell, runs)
        }
    let raise kind cell = raiseWith kind cell Array.empty

    let patchCells (cells : vscell array) = 
        let prev = !state 
        let next = cells |> vscells
        
        for oldcell in prev do
            if not (next.Contains oldcell) then do
                raise ChangeEvents.Removed oldcell

        for newcell in cells do
            match prev.TryGetValue(newcell) with
            | true, oldcell when oldcell.range = newcell.range -> 
                raise ChangeEvents.Unchanged newcell                
            | true, _ -> 
                raise ChangeEvents.Moved newcell
            | false, _ -> 
                raise ChangeEvents.Added newcell

        state := next
        
    
    let handler (evt : WorksheetEvent) = async {
        let response =
            match evt with
            | Query ->
                let op = ref Noop
                if queue.TryDequeue(op) then
                    !op
                else
                    Noop
            | ChangesStaged cells ->
                patchCells cells
                Ack
            | PreEval (cell, _) ->
                raise ChangeEvents.Evaluating cell
                Ack
            | PostEval (cell, runs) ->
                raiseWith ChangeEvents.Evaluated cell runs
                Ack
            | Committed ->
                raise ChangeEvents.Committed vscell.empty
                Ack
        
        return response
    }
    
    let cts = new CancellationTokenSource()
    let server = JsonRpc.createServer name handler
            
    [<CLIEvent>]
    member _.CellChanged = events.Publish
    member _.Start() = Async.Start (server, cts.Token)
    member _.Notify (command) =
        queue.Enqueue command
    interface IDisposable with
        member _.Dispose () = cts.Cancel()
