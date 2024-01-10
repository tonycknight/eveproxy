namespace eveproxy

open System
open System.Diagnostics.CodeAnalysis

[<CLIMutable>]
type AppConfiguration =
    { zkbRedisqBaseUrl: string
      zkbRedisqQueueId: string
      zkbRedisqTtwExternal: string
      zkbRedisqTtwClient: string
      zkbApiUrl: string }

    member this.zkbRedisqUrl() =
        sprintf "%s?queueID=%s&ttw=%s" this.zkbRedisqBaseUrl this.zkbRedisqQueueId this.zkbRedisqTtwExternal

    static member emptyConfig =
        { AppConfiguration.zkbApiUrl = ""
          zkbRedisqBaseUrl = ""
          zkbRedisqQueueId = ""
          zkbRedisqTtwExternal = ""
          zkbRedisqTtwClient = "" }

    static member defaultConfig =
        { AppConfiguration.zkbRedisqBaseUrl = "https://redisq.zkillboard.com/listen.php"
          zkbRedisqQueueId = (System.Guid.NewGuid() |> sprintf "eveProxy%A")
          zkbRedisqTtwExternal = "10"
          zkbRedisqTtwClient = "10"
          zkbApiUrl = "https://zkillboard.com/api/" }


module Configuration =
    open System.Reflection

    let private configProp =
        let props =
            typeof<AppConfiguration>.GetProperties()
            |> Seq.map (fun pi -> (pi.Name, pi))
            |> Map.ofSeq

        fun n -> props |> Map.find n

    let private propValue c (pi: PropertyInfo) =
        pi.GetGetMethod().Invoke(c, [||]) :?> string

    let private configCtor =
        typeof<AppConfiguration>.GetConstructors()
        |> Seq.sortByDescending (fun c -> c.GetParameters().Length)
        |> Seq.head

    let private ctorParams = configCtor.GetParameters()

    let mergeDefaults baseConfig config =
        let propValue c = configProp >> propValue c

        let paramValues =
            ctorParams
            |> Array.map (fun p ->
                let v =
                    let cv = p.Name |> propValue config

                    if cv |> System.String.IsNullOrWhiteSpace |> not then
                        cv
                    else
                        p.Name |> propValue baseConfig

                v :> obj)

        configCtor.Invoke(paramValues) :?> AppConfiguration



type ISecretProvider =
    abstract member IsSecretValueEqual: string -> string -> bool
    abstract member GetSecretValue: string -> string

[<ExcludeFromCodeCoverage>]
type StubSecretProvider(secrets: (string * string) seq) =

    let secrets = secrets |> Map.ofSeq

    new() = StubSecretProvider([ ("apikey", "abc") ])

    interface ISecretProvider with
        member this.IsSecretValueEqual name value =
            match secrets |> Map.tryFind name with
            | Some v -> StringComparer.Ordinal.Equals(v, value)
            | _ -> false

        member this.GetSecretValue name =
            match secrets |> Map.tryFind name with
            | Some v -> v
            | _ -> ""
