namespace eveproxy.common.tests.unit.Utils

open System
open eveproxy
open FsCheck.Xunit

module CombinatorsTests =

    [<Property>]
    let ``And projects true when both true`` (x: bool, y: bool) =
        let fx = fun (_) -> x
        let fy = fun (_) -> y

        let f = fx >&&> fy

        (f "") = (x && y)


    [<Property>]
    let ``Or projects true when either true`` (x: bool, y: bool) =
        let fx = fun (_) -> x
        let fy = fun (_) -> y

        let f = fx >||> fy

        (f "") = (x || y)
