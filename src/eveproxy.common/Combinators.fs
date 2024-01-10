namespace eveproxy

[<AutoOpen>]
module Combinators =

    let (>&&>) x y = (fun (v: 'a) -> x (v) && y (v))

    let (>||>) x y = (fun (v: 'a) -> x (v) || y (v))
