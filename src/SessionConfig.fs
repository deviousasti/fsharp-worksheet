namespace FsWorksheet.Core

open System
open FSharp.Compiler.Interactive.Shell


[<Sealed>]
type InteractiveSession() =
    let mutable printers = []
    let addPrinter printer = printers <- printer :: printers

    member val FormatProvider: IFormatProvider =
        (System.Globalization.CultureInfo.InvariantCulture :> System.IFormatProvider) with get, set

    member val FloatingPointFormat = "g10" with get, set
    member val ShowDeclarationValues = true with get, set
    member val ShowIEnumerable = true with get, set
    member val ShowProperties = true with get, set
    member val PrintSize = 15000 with get, set
    member val PrintDepth = 100 with get, set
    member val PrintWidth = 80 with get, set
    member val PrintLength = 100 with get, set
    member val CommandLineArgs = Environment.GetCommandLineArgs() with get, set

    member _.AddedPrinters = printers

    member _.AddPrinter(printer: 'T -> string) =
        addPrinter (Choice1Of2(typeof<'T>, (fun (x: obj) -> printer (unbox x))))

    member _.AddPrintTransformer(printer: 'T -> obj) =
        addPrinter (Choice2Of2(typeof<'T>, (fun (x: obj) -> printer (unbox x))))

type WorksheetSessionConfig(session: InteractiveSession) =
    inherit FsiEvaluationSessionHostConfig()

    let defaultConfig =
        FsiEvaluationSession.GetDefaultConfiguration()

    override val FormatProvider = session.FormatProvider
    override val FloatingPointFormat = session.FloatingPointFormat
    override _.AddedPrinters = session.AddedPrinters
    override val ShowDeclarationValues = session.ShowDeclarationValues
    override val ShowIEnumerable = session.ShowIEnumerable
    override val ShowProperties = session.ShowProperties
    override val PrintSize = session.PrintSize
    override val PrintDepth = session.PrintDepth
    override val PrintWidth = session.PrintWidth
    override val PrintLength = session.PrintLength

    override _.ReportUserCommandLineArgs(args) =
        defaultConfig.ReportUserCommandLineArgs(args)

    override _.StartServer(fsiServerName) =
        defaultConfig.StartServer(fsiServerName)

    override _.EventLoopRun() = defaultConfig.EventLoopRun()
    override _.EventLoopInvoke(f: unit -> 'T) = defaultConfig.EventLoopInvoke(f)

    override _.EventLoopScheduleRestart() =
        defaultConfig.EventLoopScheduleRestart()

    override _.UseFsiAuxLib = false
    override _.GetOptionalConsoleReadLine(_probe) = None
