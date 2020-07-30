namespace FSharp.Compiler

type range = Range.range

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



