namespace FSharp.Compiler.SourceCodeServices
open System
open System.IO
open System.Text
open FSharp.Compiler
open FSharp.Compiler.SourceCodeServices
open FSharp.Compiler.SyntaxTree

type range = Range.range

module AstTraversal = 
    
    let rec unwrapModule moduleIdent decls = seq {
        for decl in decls do
            match decl with 
            | SynModuleDecl.NestedModule(isRecursive = isRecursive; decls = decls; moduleInfo = ComponentInfo(longId = ident)) ->                
                yield! unwrapModule (moduleIdent @ ident) decls
            | decl -> yield moduleIdent, decl
    }        

    let unwrapNamespace modules = seq {
        for synmod in modules do
            let (SynModuleOrNamespace(decls = decls; longId = ident; range = range)) = synmod
            // implicit top-level module
            yield! unwrapModule [] decls
    }

    let declarationsFor (file: FSharpParseFileResults) =         
        match file.ParseTree with
            | (Some (ParsedInput.ImplFile(implFile))) ->
                let (ParsedImplFileInput(fn, script, name, pragmas, directives, modules, _)) = implFile
                unwrapNamespace modules
            | _ -> failwith "F# Interface file (*.fsi) not supported."
    
    let isLeadingWhiteSpace (source: FSharp.Compiler.Text.ISourceText) (range: range) =
        source.GetLineString(range.StartLine - 1).AsSpan(0, range.StartColumn).IsWhiteSpace()

    let rangeToText (source: FSharp.Compiler.Text.ISourceText) (range: range) = 
        if range.StartLine = range.EndLine then
            if isLeadingWhiteSpace source range then
                source.GetLineString(range.StartLine - 1)
            else
                source.GetLineString(range.StartLine - 1).Substring(range.StartColumn, range.EndColumn - range.StartColumn)
        else
            let sb = new StringBuilder()            

            if isLeadingWhiteSpace source range then
                sb.Append(source.GetLineString(range.StartLine - 1)).AppendLine() |> ignore
            else
                sb.Append(source.GetLineString(range.StartLine - 1).AsSpan(range.StartColumn)).AppendLine() |> ignore
            
            for i in range.StartLine..(range.EndLine - 2) do
                sb.AppendLine(source.GetLineString(i)) |> ignore
           
            sb.Append(source.GetLineString(range.EndLine - 1).AsSpan(0, range.EndColumn)).AppendLine() |> ignore
           
            sb.ToString()
    
    let astToText (source: FSharp.Compiler.Text.ISourceText) (decl: SynModuleDecl) (scope: LongIdent) =
        match decl with
        | SynModuleDecl.Open (ident, range) -> sprintf "open %s" (rangeToText source range)
        | SynModuleDecl.HashDirective(ParsedHashDirective("pragma", ["worksheet"], _), _) ->      
            //ignore our own pragma directive
            ""
        | SynModuleDecl.HashDirective(ParsedHashDirective("load", [filename], _), range) ->           
            // retarget script path
            let root = Path.GetDirectoryName range.FileName
            let fullPath = if Path.IsPathRooted filename then filename else Path.Combine (root, filename)
            sprintf "#load \"%s\"" fullPath
        | decl -> 
            let text = rangeToText source decl.Range
            (scope 
            |> Seq.mapi(fun i id -> sprintf "%smodule %s = \n" (String.replicate i "    ") id.idText) 
            |> String.concat "")                
                + text
            
        