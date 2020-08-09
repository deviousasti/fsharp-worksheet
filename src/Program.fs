open System
open FsWorksheet
open FsWorksheet.FsWatch
open System.IO

[<EntryPoint>]
let main argv =
    if Array.isEmpty argv then exit -1
    let file = Path.GetFullPath (Array.head argv)
    printfn "F# Worksheet\nWatching: %s" file
    use diposable = 
        FsWatch.watchFile { 
            filename = file
            events = 
            {   Worksheet.Events.none with
                    onBeforeEval = Print.sourceToConsole 
                    onAfterEval = Print.resultToConsole 
            }
        }
        
    Console.ReadLine() |> ignore
    0