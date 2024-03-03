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

    let toBool (defaultValue: bool) (value: string) =
        match bool.TryParse(value) with
        | true, x -> x
        | _ -> defaultValue

    let toTimeSpan (defaultValue: TimeSpan) (value: string) =
        match TimeSpan.TryParse(value) with
        | true, x -> x
        | _ -> defaultValue

    let toLine (values: seq<string>) =
        String.Join(Environment.NewLine, values)

    let join (delim: string) (values: seq<string>) = System.String.Join(delim, values)

    let split (delim: string) (value: string) =
        value.Split(delim.ToCharArray(), StringSplitOptions.RemoveEmptyEntries)

    let toLower (value: string) = value.ToLower()

    let trim (value: string) = value.Trim()

    let defaultIf (comparand: string) (defaultValue: string) (value: string) =
        if value = comparand then defaultValue else value

    let fromGzip (value: System.IO.Stream) =
        let bufferSize = 512
        let buffer = Array.create<byte> bufferSize 0uy
        use outStream = new System.IO.MemoryStream()

        use decomp =
            new System.IO.Compression.GZipStream(value, System.IO.Compression.CompressionMode.Decompress)

        let mutable len = -1

        while len <> 0 do
            len <- decomp.Read(buffer)

            if len > 0 then
                outStream.Write(buffer, 0, len)

        outStream.Seek(0, System.IO.SeekOrigin.Begin) |> ignore
        use reader = new System.IO.StreamReader(outStream)
        reader.ReadToEnd()

    let toGzip (value: string) =
        let bytes = System.Text.Encoding.UTF8.GetBytes(value)
        use outStream = new System.IO.MemoryStream()

        use comp =
            new System.IO.Compression.GZipStream(outStream, System.IO.Compression.CompressionMode.Compress)

        comp.Write(bytes)
        comp.Flush()
        outStream.Seek(0, System.IO.SeekOrigin.Begin) |> ignore
        outStream.ToArray()

    let appendIfMissing (suffix: string) (value: string) =
        if value.EndsWith(suffix) |> not then
            $"{value}{suffix}"
        else
            value

    let (|NullOrWhitespace|_|) value =
        if String.IsNullOrWhiteSpace value then Some value else None

module Option =
    let nullToOption (value: obj) =
        if value = null then None else Some value

    let ofNull<'a> (value: 'a) =
        if Object.ReferenceEquals(value, null) then
            None
        else
            Some value

    let reduceMany (values: seq<'a option>) =
        values |> Seq.filter Option.isSome |> Seq.map Option.get

module Threading =
    let toTaskResult (value) =
        System.Threading.Tasks.Task.FromResult(value)

    let whenAll<'a> (tasks: System.Threading.Tasks.Task<'a>[]) =
        System.Threading.Tasks.Task.WhenAll(tasks)

module Validators =
    let isUrl value =
        try
            new Uri(value) |> ignore
            true
        with ex ->
            false

    let isEmptyString = String.IsNullOrWhiteSpace

    let isNonEmptyString = isEmptyString >> not

    let isMinimumValueInteger minimum (value: string) =
        match Int32.TryParse value with
        | true, x when x >= minimum -> true
        | _ -> false

    let isTimeSpan (value: string) = TimeSpan.TryParse value |> fst

    let mustBe (validate: string -> bool) (error: string) (value) =
        if value |> validate |> not then Some error else None

type ITimeProvider =
    abstract member GetUtcNow: unit -> System.DateTime

[<ExcludeFromCodeCoverage>]
type TimeProvider() =
    interface ITimeProvider with
        member _.GetUtcNow() = System.DateTime.UtcNow

[<ExcludeFromCodeCoverage>]
module Uri =
    let tryParse (uri: string) =
        match Uri.IsWellFormedUriString(uri, UriKind.Absolute) with
        | true -> new Uri(uri) |> Some
        | _ -> None

module Dictionary =
    open System.Collections.Generic

    let toDictionary<'v> (values: (string * 'v) seq) =
        let pairs = values |> Seq.map (fun (n, v) -> new KeyValuePair<string, 'v>(n, v))
        new Dictionary<string, 'v>(pairs)

    let tryFind<'v> key (dictionary: Dictionary<string, 'v>) =
        match dictionary.TryGetValue key with
        | (true, v) -> Some v
        | _ -> None

[<ExcludeFromCodeCoverage>]
module Io =
    open System.IO

    let toFullPath (path: string) =
        if not <| Path.IsPathRooted(path) then
            let wd = Environment.CurrentDirectory

            Path.Combine(wd, path)
        else
            path

    let normalise (path: string) = Path.GetFullPath(path)

    let fileExists (path: string) = File.Exists(path)

[<ExcludeFromCodeCoverage>]
module Args =

    let getValue prefix (args: seq<string>) =
        let arg =
            args
            |> Seq.filter (fun a -> a.StartsWith(prefix, StringComparison.InvariantCultureIgnoreCase))
            |> Seq.tryHead

        match arg with
        | Some arg -> arg.Substring(prefix.Length).Trim() |> Some
        | None -> None
