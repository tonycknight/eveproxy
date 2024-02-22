namespace eveproxy.common.tests.unit

open System
open eveproxy
open FsCheck

[<AutoOpen>]
module TestDataGenerators=    

    let isAlphaNumeric (value: string) =
        value |> Seq.forall (Char.IsLetter ||>> Char.IsNumber)

    let isNotNullOrEmpty = String.IsNullOrEmpty >> not
        
type AlphaNumericString =

    static member Generate() =
        Arb.Default.String() |> Arb.filter (isNotNullOrEmpty &&>> isAlphaNumeric)

type AlphaNumericStringSingletonArray =

    static member Generate() =
        Arb.generate<string>
        |> Gen.filter (isNotNullOrEmpty &&>> isAlphaNumeric)
        |> Gen.map (fun s -> [| s |])
        |> Arb.fromGen    