namespace FsWorksheet

open System
open System.Collections.Concurrent
open Remoting

type ChangeEvents =
| Unchanged     = 0 
| Added         = 1
| Moved         = 2
| Removed       = 3
| Evaluating    = 4
| Evaluated     = 5

type Worksheet (name, file) =
    let queue = new BlockingCollection<_>()
    let state = ref (vscells ())
    //let ranges = new RangeTree<vspos, CellView>(API.posComparer)
    let events = new Event<_>()
    let raiseWith (kind : ChangeEvents) (cell : vscell) runs = events.Trigger struct (kind, cell, runs)
    let raise kind cell = raiseWith kind cell Array.empty

    let patchCells (cells : vscell array) = 
        let prev = !state 
        let next = cells |> vscells

        for newcell in cells do
            match prev.TryGetValue(newcell) with
            | true, oldcell when oldcell.range = newcell.range -> 
                raise ChangeEvents.Unchanged newcell                
            | true, _ -> 
                raise ChangeEvents.Moved newcell
            | false, _ -> 
                raise ChangeEvents.Added newcell
        
        // remove all common
        prev.ExceptWith next
        
        for oldcell in prev do
            raise ChangeEvents.Removed oldcell

        state := next
        
    
    let handler (evt : WorksheetEvent) = async {
        let! token = Async.CancellationToken
        let response =
            match evt with
            | Query ->
                let op = ref Noop
                if queue.TryTake(op, 2000, token) then
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

        return response
    }
    
    let client = Rpc.createHost name handler
    
    member _.CellChanged = events.Publish
    member _.Notify (command) =
        queue.Add command
    interface IDisposable with
        member _.Dispose () = 
            queue.Dispose()
            (client :> IDisposable).Dispose()
