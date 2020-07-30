namespace FsWorksheet.Core

open System
open System.IO
open System.Text

type Run = ConsoleColor * string

type RunWriter () = 
    inherit TextWriter()
    let runs = ResizeArray<Run>()
    let mutable last = (ConsoleColor.Black, "")

    let writeRun str = 
        let fg = Console.ForegroundColor
        let lastFg, lastStr = last
        let current = 
            if lastFg = fg && runs.Count > 0 then
                runs.RemoveAt (runs.Count - 1)
                fg, (lastStr + str)        
            else
                fg, str
        
        runs.Add current
        last <- current

    override this.Encoding with get () = Encoding.Default
    override this.Write(str: string) = 
        writeRun str            
    override this.Write(ch: char) = 
        writeRun (string ch)
    member _.GetRuns() = runs.ToArray()
    member _.Reset() = runs.Clear()

    static member PrintToConsole(runs) =
        runs |> Seq.iter (fun (fg, text) -> 
            Console.ForegroundColor <- fg
            Console.Write (text : string)
        )
        Console.ResetColor()

    member _.PrintToConsole() = RunWriter.PrintToConsole(runs)
