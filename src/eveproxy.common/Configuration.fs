namespace eveproxy

open System
open System.Diagnostics.CodeAnalysis
open eveproxy.Validators
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection

[<CLIMutable>]
type AppConfiguration =
    { hostUrls: string
      allowExternalTraffic: string
      zkbRedisqBaseUrl: string
      zkbRedisqQueueId: string
      zkbRedisqTtwExternal: string
      zkbRedisqTtwClient: string
      zkbApiUrl: string
      redisqSessionMaxAge: string
      mongoServer: string
      mongoDbName: string
      mongoUserName: string
      mongoPassword: string }

    member this.ZkbRedisqUrl() =
        sprintf "%s?queueID=%s&ttw=%s" this.zkbRedisqBaseUrl this.zkbRedisqQueueId this.zkbRedisqTtwExternal

    member this.ClientRedisqTtw() =
        this.zkbRedisqTtwClient |> Strings.toInt 10

    member this.RedisqSessionMaxAge() =
        this.redisqSessionMaxAge |> Strings.toTimeSpan (TimeSpan.FromHours 3)

    static member emptyConfig =
        { AppConfiguration.hostUrls = ""
          allowExternalTraffic = false.ToString()
          zkbApiUrl = ""
          zkbRedisqBaseUrl = ""
          zkbRedisqQueueId = ""
          zkbRedisqTtwExternal = ""
          zkbRedisqTtwClient = ""
          redisqSessionMaxAge = ""
          mongoServer = ""
          mongoDbName = ""
          mongoUserName = ""
          mongoPassword = "" }

    static member defaultConfig =
        { AppConfiguration.hostUrls = "http://+:8080"
          allowExternalTraffic = false.ToString()
          zkbRedisqBaseUrl = "https://redisq.zkillboard.com/listen.php"
          zkbRedisqQueueId = (System.Guid.NewGuid() |> sprintf "eveProxy%A")
          zkbRedisqTtwExternal = "10"
          zkbRedisqTtwClient = "10"
          zkbApiUrl = "https://zkillboard.com/api/"
          redisqSessionMaxAge = TimeSpan.FromHours(3).ToString()
          mongoServer = "127.0.0.1"
          mongoDbName = "eveproxy"
          mongoUserName = ""
          mongoPassword = "" }

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

    let validate (config: AppConfiguration) =
        let validUrl value =
            try
                new Uri(value) |> ignore
                true
            with ex ->
                false

        let nonEmptyString = String.IsNullOrWhiteSpace >> not

        let positiveInteger (value: string) =
            match Int32.TryParse value with
            | true, x when x >= 0 -> true
            | _ -> false

        let timeSpan (value: string) =
            match TimeSpan.TryParse value with
            | true, _ -> true
            | _ -> false

        let errors =
            seq {
                config.hostUrls
                |> mustBe
                    nonEmptyString
                    $"{nameof Unchecked.defaultof<AppConfiguration>.hostUrls} must be a non-empty string."

                config.zkbRedisqBaseUrl
                |> mustBe
                    (nonEmptyString >&&> validUrl)
                    $"{nameof Unchecked.defaultof<AppConfiguration>.zkbRedisqBaseUrl} must be a non-empty string."

                config.zkbRedisqTtwExternal
                |> mustBe
                    positiveInteger
                    $"{nameof Unchecked.defaultof<AppConfiguration>.zkbRedisqTtwExternal} must be a positive integer."

                config.zkbRedisqTtwClient
                |> mustBe
                    positiveInteger
                    $"{nameof Unchecked.defaultof<AppConfiguration>.zkbRedisqTtwClient} must be a positive integer."

                config.zkbApiUrl
                |> mustBe
                    (nonEmptyString >&&> validUrl)
                    $"{nameof Unchecked.defaultof<AppConfiguration>.zkbApiUrl} must be a valid URL."

                config.redisqSessionMaxAge
                |> mustBe
                    timeSpan
                    $"{nameof Unchecked.defaultof<AppConfiguration>.redisqSessionMaxAge} must be a valid timespan (HH:mm:ss)."

                config.mongoServer
                |> mustBe
                    nonEmptyString
                    $"{nameof Unchecked.defaultof<AppConfiguration>.mongoServer} must be a non-empty string."

                config.mongoDbName
                |> mustBe
                    nonEmptyString
                    $"{nameof Unchecked.defaultof<AppConfiguration>.mongoDbName} must be a non-empty string."

                config.mongoUserName
                |> mustBe
                    nonEmptyString
                    $"{nameof Unchecked.defaultof<AppConfiguration>.mongoUserName} must be a non-empty string."

                config.mongoPassword
                |> mustBe
                    nonEmptyString
                    $"{nameof Unchecked.defaultof<AppConfiguration>.mongoPassword} must be a non-empty string."
            }
            |> Option.reduceMany
            |> Strings.toLine

        if errors.Length > 0 then
            failwith $"Errors found in configuration:{Environment.NewLine}{errors}"

        config

    let create (sp: System.IServiceProvider) =
        sp
            .GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>()
            .Get<AppConfiguration>()
        |> mergeDefaults AppConfiguration.defaultConfig
        |> validate

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
