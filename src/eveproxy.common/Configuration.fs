namespace eveproxy

open System
open System.Diagnostics.CodeAnalysis
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection

[<CLIMutable>]
type AppConfiguration =
    { hostUrls: string
      zkbRedisqBaseUrl: string
      zkbRedisqQueueId: string
      zkbRedisqTtwExternal: string
      zkbRedisqTtwClient: string
      zkbApiUrl: string
      redisqSessionMaxAge: string }

    member this.ZkbRedisqUrl() =
        sprintf "%s?queueID=%s&ttw=%s" this.zkbRedisqBaseUrl this.zkbRedisqQueueId this.zkbRedisqTtwExternal

    member this.ClientRedisqTtw() =
        this.zkbRedisqTtwClient |> Strings.toInt 10

    member this.RedisqSessionMaxAge() =
        this.redisqSessionMaxAge |> Strings.toTimeSpan (TimeSpan.FromHours 3)

    static member emptyConfig =
        { AppConfiguration.hostUrls = ""
          zkbApiUrl = ""
          zkbRedisqBaseUrl = ""
          zkbRedisqQueueId = ""
          zkbRedisqTtwExternal = ""
          zkbRedisqTtwClient = ""
          redisqSessionMaxAge = "" }

    static member defaultConfig =
        { AppConfiguration.hostUrls = "http://+:8080"
          zkbRedisqBaseUrl = "https://redisq.zkillboard.com/listen.php"
          zkbRedisqQueueId = (System.Guid.NewGuid() |> sprintf "eveProxy%A")
          zkbRedisqTtwExternal = "10"
          zkbRedisqTtwClient = ""
          zkbApiUrl = "https://zkillboard.com/api/"
          redisqSessionMaxAge = "" }


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

    let create (sp: System.IServiceProvider) =
        sp
            .GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>()
            .Get<AppConfiguration>()
        |> mergeDefaults AppConfiguration.defaultConfig

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
