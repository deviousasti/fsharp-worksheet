namespace Worksheet.Core

open System
open System.IO
open FSharp.Compiler
open FSharp.Compiler.Interactive.Shell
open Microsoft.FSharp.Control

open FSharp.Compiler.Range
open FSharp.Compiler.SourceCodeServices
open FSharp.Compiler.Text

type range = Range.range

type EvalContext (?config : FsiEvaluationSessionHostConfig, ?filename : string) =                    

    let inStream = new StringReader("")
    let outStream = new RunWriter() // Console.Out  // new StringWriter(sbOut)
    let errStream = new RunWriter() // Console.Out // new StringWriter(sbErr)

    let stdOut = Console.Out
    let stdErr = Console.Error

    let fsiConfig = defaultArg config (FsiEvaluationSession.GetDefaultConfiguration())   

    
    let argv = [| "C:\\fsi.exe" |]
    let allArgs = Array.append argv [|"--noninteractive"; "--nologo"; "--fsi-server:fswatch" |]       

    let fsiSession = FsiEvaluationSession.Create(fsiConfig, allArgs, inStream, outStream, errStream)
    
    let sourceFile = defaultArg filename "stdin.fsx"   

    let checkAsNewProject sourceFile (source : ISourceText) =         
        async {                 
            let projectOptsAsync = fsiSession.InteractiveChecker.GetProjectOptionsFromScript(sourceFile, Text.SourceText.ofString "") 
            let! (opts, _err) = projectOptsAsync            
            let! (parseResults, checkAnswer) = fsiSession.InteractiveChecker.ParseAndCheckFileInProject(sourceFile, 1, source, opts)           

            let checkResults =
                match checkAnswer with
                | FSharpCheckFileAnswer.Aborted ->         
                    failwith "Aborted"
                | FSharpCheckFileAnswer.Succeeded checkResults ->
                    checkResults

            return parseResults, checkResults
        }

    let checkInteraction source = async {      
       let! (parseResults, checkResults, _) = fsiSession.ParseAndCheckInteraction source
       return parseResults, checkResults
    }
        

    let sanitise obj =
        System.Text.RegularExpressions.Regex.Replace(obj.ToString(), @"FSI_\d\d\d\d\.?", "") + outStream.NewLine

    //fsiSession.InteractiveChecker.com
    let evalInteraction text = 
        async { 
            let! token = Async.CancellationToken
            outStream.Reset()
            errStream.Reset()               

            try
          
                Console.SetOut outStream
                Console.SetError errStream

                let value, errInfo = fsiSession.EvalInteractionNonThrowing(text, token)
                let result = 
                    match value, errInfo with 
                    | Choice1Of2 value, _ -> Ok (outStream.GetRuns(), value)
                    | Choice2Of2 exn, [||] -> Error (errStream.GetRuns(), [| ConsoleColor.Red, sanitise exn |])
                    | Choice2Of2 exn, err -> Error (errStream.GetRuns(), (err |> Array.map(fun e -> ConsoleColor.Red, sanitise e.Message )))

                return result
            finally

              Console.SetOut stdOut
              Console.SetError stdErr
        
        
        }    

    let checkSource source =
        fsiSession.ParseAndCheckInteraction (source)

    member _.Eval (text) = evalInteraction text
    member _.Check (text) = checkInteraction text
    member _.Check (text, filename) = checkAsNewProject filename text

    interface IDisposable with
        override _.Dispose() = 
            (fsiSession :> IDisposable).Dispose()                


