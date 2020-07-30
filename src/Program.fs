open System
open FsWorksheet

[<EntryPoint>]
let main argv =
    let file = Array.head argv
    use diposable = FsWatch.watchFile file Worksheet.printCellToConsole 
    Console.ReadLine() |> ignore
    0