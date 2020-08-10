open System
open FsWorksheet
open System.IO

[<EntryPoint>]
let main argv =
    let name, file = argv.[0], argv.[1]
    use worksheet = new WorksheetModel(name, file, fun task -> Async.RunSynchronously task)
    use subscription = 
        worksheet.CellChanged 
        |> Observable.subscribe (fun struct (evt, cell, _) -> 
            printfn "%A %s" evt (string cell)
        )           
    
    while true do
        printfn "Sending..."
        worksheet.Notify(Compute (File.ReadAllText(file)))
        Console.ReadLine() |> ignore
    0 // return an integer exit code
