namespace eveproxy

open System
open System.Diagnostics.CodeAnalysis

module Strings =
    let leftSnippet (len: int) (value: string) =
        if value.Length < len then
            value
        else
            value.Substring(0, len) + "..."

    let toInt (defaultValue: int) (value: string) =
        match Int32.TryParse(value) with
        | true, x -> x
        | _ -> defaultValue

    let toTimeSpan (defaultValue: TimeSpan) (value: string) =
        match TimeSpan.TryParse(value) with
        | true, x -> x
        | _ -> defaultValue

    let toLine (values: seq<string>) =
        String.Join(Environment.NewLine, values)

module Option =
    let nullToOption (value: obj) =
        if value = null then None else Some value

    let reduceMany (values: seq<'a option>) =
        values |> Seq.filter Option.isSome |> Seq.map Option.get

module Threading =
    let toTaskResult (value) =
        System.Threading.Tasks.Task.FromResult(value)

    let whenAll<'a> (tasks: System.Threading.Tasks.Task<'a>[]) =
        System.Threading.Tasks.Task.WhenAll(tasks)

module Validators =
    let mustBe (validate: string -> bool) (error: string) (value) =
        if value |> validate |> not then Some error else None

type ITimeProvider =
    abstract member GetUtcNow: unit -> System.DateTime

[<ExcludeFromCodeCoverage>]
type TimeProvider() =
    interface ITimeProvider with
        member _.GetUtcNow() = System.DateTime.UtcNow
