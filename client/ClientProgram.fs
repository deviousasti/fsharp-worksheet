open System
open FsWorksheet

[<EntryPoint>]
let main argv =
    let name, file = argv.[0], argv.[1]
    use worksheet = new Worksheet(name, file)
    use subscription = 
        worksheet.CellChanged 
        |> Observable.subscribe (printfn "%A")           
    
    Console.ReadLine() |> ignore
    0 // return an integer exit code
