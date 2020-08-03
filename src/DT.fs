namespace FsWorksheet

open System

type vspos = { Line: int; Col: int }
type vsrange = { From: vspos; To: vspos }
type Run = ConsoleColor * string
type Runs = Run []
type eqhash = int32
type vscell = { hash: eqhash; range: vsrange }

type WorksheetCommand =
| Compute of source: string
| ForceEvalCellAt of vspos
| ForceEvalRange of vsrange
| Interrupt
| Noop
| Exit


type WorksheetEvent =
| Query
| PreEval of vsrange * eqhash
| PostEval of vsrange * eqhash * Runs 
| CellsChanged of vscell []
