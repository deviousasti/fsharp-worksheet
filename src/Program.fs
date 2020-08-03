open System
open FsWorksheet
open FsWorksheet.FsWatch

[<EntryPoint>]
let main argv =
    if Array.isEmpty argv then exit -1
    let file = Array.head argv
    printfn "F# Worksheet\nWatching: %s" file
    use diposable = 
        FsWatch.watchFile { 
            filename = file
            onBeforeEvaluation = Print.sourceToConsole 
            onAfterEvaluation = Print.resultToConsole 
            onStateChanged = ignore
        }
        
    Console.ReadLine() |> ignore
    0