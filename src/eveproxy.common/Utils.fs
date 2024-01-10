namespace eveproxy

open System.Diagnostics.CodeAnalysis

module Strings =
    let leftSnippet (len: int) (value: string) =
        if value.Length < len then
            value
        else
            value.Substring(0, len) + "..."

    let toInt (defaultValue: int) (value: string) =
        match System.Int32.TryParse(value) with
        | true, x -> x
        | _ -> defaultValue

module Option =
    let nullToOption (value: obj) =
        if value = null then None else Some value

module Threading =
    let toTaskResult (value) =
        System.Threading.Tasks.Task.FromResult(value)


type ITimeProvider =
    abstract member GetUtcNow: unit -> System.DateTime

[<ExcludeFromCodeCoverage>]
type TimeProvider() =
    interface ITimeProvider with
        member _.GetUtcNow() = System.DateTime.UtcNow
