namespace eveproxy

open System
open System.Diagnostics.CodeAnalysis
open eveproxy.Validators
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging

type IKeyValueProvider =
    abstract member IsValueEqual: string -> string -> bool
    abstract member GetValue: string -> string option
    abstract member SetValue: string -> string -> unit

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
      mongoDbName: string
      mongoConnection: string }

    member this.ZkbRedisqUrl() =
        sprintf "%s?queueID=%s&ttw=%s" this.zkbRedisqBaseUrl this.zkbRedisqQueueId this.zkbRedisqTtwExternal

    member this.ClientRedisqTtw() =
        this.zkbRedisqTtwClient |> Strings.toInt 10

    member this.RedisqSessionMaxAge() =
        this.redisqSessionMaxAge |> Strings.toTimeSpan (TimeSpan.FromHours 3)

    static member emptyConfig =
        { AppConfiguration.hostUrls = ""
          allowExternalTraffic = true.ToString()
          zkbApiUrl = ""
          zkbRedisqBaseUrl = ""
          zkbRedisqQueueId = ""
          zkbRedisqTtwExternal = ""
          zkbRedisqTtwClient = ""
          redisqSessionMaxAge = ""
          mongoDbName = ""
          mongoConnection = "" }

    static member defaultConfig =
        { AppConfiguration.hostUrls = "http://+:8080"
          allowExternalTraffic = true.ToString()
          zkbRedisqBaseUrl = "https://redisq.zkillboard.com/listen.php"
          zkbRedisqQueueId = (System.Guid.NewGuid() |> sprintf "eveProxy%A")
          zkbRedisqTtwExternal = "10"
          zkbRedisqTtwClient = "10"
          zkbApiUrl = "https://zkillboard.com/api/"
          redisqSessionMaxAge = TimeSpan.FromHours(3).ToString()
          mongoDbName = "eveproxy"
          mongoConnection = "" }

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

    let validationErrors (config: AppConfiguration) =
        seq {
            config.hostUrls
            |> mustBe
                isNonEmptyString
                $"{nameof Unchecked.defaultof<AppConfiguration>.hostUrls} must be a non-empty string."

            config.zkbRedisqBaseUrl
            |> mustBe
                (isNonEmptyString &&>> isUrl)
                $"{nameof Unchecked.defaultof<AppConfiguration>.zkbRedisqBaseUrl} must be a valid URL."

            config.zkbRedisqTtwExternal
            |> mustBe
                isPositiveInteger
                $"{nameof Unchecked.defaultof<AppConfiguration>.zkbRedisqTtwExternal} must be a positive integer."

            config.zkbRedisqTtwClient
            |> mustBe
                isPositiveInteger
                $"{nameof Unchecked.defaultof<AppConfiguration>.zkbRedisqTtwClient} must be a positive integer."

            config.zkbApiUrl
            |> mustBe
                (isNonEmptyString &&>> isUrl)
                $"{nameof Unchecked.defaultof<AppConfiguration>.zkbApiUrl} must be a valid URL."

            config.redisqSessionMaxAge
            |> mustBe
                isTimeSpan
                $"{nameof Unchecked.defaultof<AppConfiguration>.redisqSessionMaxAge} must be a valid timespan (HH:mm:ss)."

            config.mongoDbName
            |> mustBe
                isNonEmptyString
                $"{nameof Unchecked.defaultof<AppConfiguration>.mongoDbName} must be a non-empty string."

            config.mongoConnection
            |> mustBe
                isNonEmptyString
                $"{nameof Unchecked.defaultof<AppConfiguration>.mongoConnection} must be a non-empty string."
        }
        |> Option.reduceMany

    let validate (config: AppConfiguration) =

        let errors = config |> validationErrors |> Strings.toLine

        if errors.Length > 0 then
            failwith $"Errors found in configuration:{Environment.NewLine}{errors}"

        config

    let create (sp: System.IServiceProvider) =
        sp
            .GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>()
            .Get<AppConfiguration>()
        |> mergeDefaults AppConfiguration.defaultConfig
        |> validate

    let applyKeyValues (kvs: IKeyValueProvider) (config: AppConfiguration) =
        let k = nameof Unchecked.defaultof<AppConfiguration>.zkbRedisqQueueId

        match kvs.GetValue k with
        | Some qId -> { config with zkbRedisqQueueId = qId }
        | None ->
            kvs.SetValue k config.zkbRedisqQueueId
            config

[<ExcludeFromCodeCoverage>]
type MemoryKeyValueProvider(secrets: (string * string) seq) =

    let secrets = Dictionary.toDictionary secrets

    new() = MemoryKeyValueProvider([ ("apikey", "abc") ])

    interface IKeyValueProvider with
        member this.IsValueEqual name value =
            match secrets |> Dictionary.tryFind name with
            | Some v -> StringComparer.Ordinal.Equals(v, value)
            | _ -> false

        member this.GetValue name = secrets |> Dictionary.tryFind name

        member this.SetValue name value = secrets.[name] <- value

open MongoDB.Bson

[<ExcludeFromCodeCoverage>]
type MongoKeyValueProvider(logger: ILoggerFactory, config: AppConfiguration) =

    [<Literal>]
    let collectionName = "keyvalues"

    let logger = logger.CreateLogger<MongoKeyValueProvider>()

    let mongoCol =
        eveproxy.Mongo.initCollection "" config.mongoDbName collectionName config.mongoConnection


    let getValue name =
        task {
            let! x = sprintf "{'_id': '%s' }" name |> eveproxy.Mongo.getSingle<BsonDocument> mongoCol

            return
                match x with
                | Some bd -> bd.["value"].ToString() |> Some
                | _ -> None
        }

    let setValue (name: string) (value: string) =
        task {
            let doc = new MongoDB.Bson.BsonDocument()
            doc.["_id"] <- name
            doc.["value"] <- value

            let! x = doc |> eveproxy.Mongo.upsert mongoCol
            ignore x
        }

    interface IKeyValueProvider with
        member this.IsValueEqual name value =
            let v = (getValue name).Result

            match v with
            | Some v -> StringComparer.Ordinal.Equals(v, value)
            | _ -> false

        member this.GetValue name = (getValue name).Result

        member this.SetValue name value = (setValue name value).Result |> ignore
