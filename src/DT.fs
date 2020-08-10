namespace FsWorksheet

open System
open System.Collections.Generic

type vspos = { Line: int; Col: int }
type vsrange = { From: vspos; To: vspos }
type eqhash = int32

[<CustomEquality; NoComparison>]
type vscell = { eqHash: eqhash; range: vsrange } with
    override this.GetHashCode() = this.eqHash
    override this.Equals(other) = 
        match other with
        | :? vscell as cell -> cell.eqHash = this.eqHash
        | _ -> false
    override this.ToString() = 
        let from, upto = this.range.From, this.range.To
        sprintf "%x (%d, %d) - (%d, %d)" this.eqHash from.Line from.Col upto.Line upto.Col
    static member empty = {eqHash = 0; range = { From = { Line = 0; Col = 0 }; To = { Line = 0; Col = 0 } }}
    interface IEquatable<vscell> with
        member this.Equals(cell) = this.eqHash = cell.eqHash


type Run = ConsoleColor * string
type Runs = Run array
type vscells = HashSet<vscell>

type WorksheetCommand =
| Compute of source: string
| ForceEvalCellAt of vspos
| ForceEvalRange of vsrange
| Interrupt
| Noop
| Timedout
| Ack
| Exit


type WorksheetEvent =
| Query
| ChangesStaged of vscell array
| PreEval of vscell * string
| PostEval of vscell * Runs 
| Committed


module API =
    let posComparer = 
        Comparer.Create (fun (x : vspos) y ->                
                match compare x.Line y.Line with
                | 0 -> compare x.Col y.Col
                | v -> v
        )