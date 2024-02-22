namespace eveproxy.common.tests.unit.Utils

open System
open FsCheck
open FsCheck.Xunit
open eveproxy

module StringsTests =

    [<Property>]
    let ``toInt parses integers returns value`` (x: int) = (x.ToString() |> Strings.toInt -x) = x

    [<Property>]
    let ``toInt parses non-integers returns default`` (x: int) (defaultValue: int) =
        ($"{x}A" |> Strings.toInt defaultValue) = defaultValue


    [<Property>]
    let ``leftSnippet retains chars`` (value: NonEmptyString) =
        let value = value.Get

        let result = value |> Strings.leftSnippet (value.Length + 1)

        result = value

    [<Property>]
    let ``leftSnippet trims chars`` (value: NonEmptyString) =
        let value = value.Get
        let len = value.Length - 1
        let expected = value.Substring(0, len) + "..."

        let result = value |> Strings.leftSnippet len

        result = expected

    [<Property>]
    let ``toLower`` (value: NonEmptyString) =
        let expected = value.Get.ToLower()

        expected = Strings.toLower value.Get

    [<Property>]
    let ``toBool`` (value: bool) =
        let v = value.ToString()

        let r = v |> Strings.toBool true

        r = value

    [<Property>]
    let ``toBool invalid boolean`` (defaultValue: bool) (value: int) =
        let v = value.ToString()

        let r = v |> Strings.toBool defaultValue

        r = defaultValue


    [<Property>]
    let ``fromGzip and toGzip are symmetric`` (value: NonEmptyString) =

        let bytes = Strings.toGzip value.Get
        use comp = new System.IO.MemoryStream(bytes)
        let decomp = comp |> Strings.fromGzip

        decomp = value.Get
