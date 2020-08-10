namespace FSharp.Compiler
open FsWorksheet

type range = Range.range
type pos = Range.pos

module Range =
    let intersects (a : range) (b: range) =
        (a.StartLine < b.EndLine) &&
        (b.StartLine < a.EndLine)
    
    let isAfter (a : range) (b: range) =
        b.StartLine > a.StartLine && b.EndLine > a.EndLine

    let rangeFor file (original : Range.range) =
        #if DEBUG
        Range.mkRange file (original.Start) (original.End)
        #else
        original
        #endif    

    let toVsPos (pos: pos) = 
        { Line = pos.Line - 1; Col = pos.Column }
    let toVsRange (range: range) = 
        { From = toVsPos range.Start; To = toVsPos range.End }

