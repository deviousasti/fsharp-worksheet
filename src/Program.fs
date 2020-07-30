open System
open FsWorksheet

[<EntryPoint>]
let main argv =
    if Array.isEmpty argv then exit -1
    let file = Array.head argv
    printfn "F# Worksheet\nWatching: %s" file
    use diposable = FsWatch.watchFile(file, Print.sourceToConsole, Print.resultToConsole)
    Console.ReadLine() |> ignore
    0