namespace eveproxy.common.tests.unit.Utils

open System
open FsCheck.Xunit
open eveproxy

module OptionTests =

    [<Property>]
    let ``nullToOption`` (x: obj) =
        match x with
        | null -> Option.nullToOption x = Option.None
        | x -> Option.nullToOption x = Option.Some x
