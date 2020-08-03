open System
open Server
open FsWorksheet
open System.Collections.Generic
open System.Collections.Concurrent

    [<CustomEquality; CustomComparison>]
    type Cell = { 
        result: Runs; 
        eqHash: int32; 
        range: vsrange;
    } with
        override this.GetHashCode() = this.eqHash
        override this.Equals(other) = 
            match other with
            | :? Cell as cell -> cell.eqHash = this.eqHash
            | _ -> false
        interface IEquatable<Cell> with
            member this.Equals(cell) = this.eqHash = cell.eqHash
        interface IComparable<Cell> with
            member this.CompareTo(cell) = this.eqHash.CompareTo(cell.eqHash)
   

type Worksheet (name, file) =
    let queue = new BlockingCollection<_>()
    let cells = HashSet<Cell>()
    
    let handler (evt : FsWorksheet.WorksheetEvent) = async {
        let! token = Async.CancellationToken
        let response =
            match evt with
            | Query ->
                let op = ref Noop
                if queue.TryTake(op, 2000, token) then
                    !op
                else
                    Noop
            | _ -> Noop

        return response
    }
    let client = Rpc.createHost name handler
    
    member _.Notify (command) =
        queue.Add command
    interface IDisposable with
        member _.Dispose () = 
            queue.Dispose()
            (client :> IDisposable).Dispose()

[<EntryPoint>]
let main argv =
    let name, file = argv.[0], argv.[1]
    
       
    
    
    Console.ReadLine() |> ignore
    0 // return an integer exit code
