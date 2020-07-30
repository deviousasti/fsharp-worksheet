namespace FSharp.Core

open System.Collections.Generic
open System


module Seq = 
    
    let toDict keySelector enumerable = 
        let map = new Dictionary<_, _>()
        for item in enumerable do
            map.[keySelector item] <- item
        map
    
    let toLookup keySelector enumerable = 
        System.Linq.Enumerable.ToLookup(enumerable, keySelector)

    let toHashSet enumerable = 
        let set = new HashSet<_>()
        for item in enumerable do
            if not (set.Add item) then do
                set.Remove item |> ignore
                set.Add item |> ignore
        set

module Disposable =
    let create action = { new IDisposable with member _.Dispose() = action () }
    let combine (disp1 : #IDisposable) (disp2 : #IDisposable) =
        create (fun () -> disp1.Dispose(); disp2.Dispose())
