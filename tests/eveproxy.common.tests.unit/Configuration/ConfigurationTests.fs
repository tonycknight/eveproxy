namespace eveproxy.common.tests.unit.Utils

open System
open eveproxy
open eveproxy.common.tests.unit
open FsCheck.Xunit

module ConfigurationTests =

    let minimumValidConfig =
        { AppConfiguration.defaultConfig with
            mongoConnection = "aaa" }

    [<Property>]
    let ``mergeDefaults merges empty to default values`` () =
        let defaultConfig = AppConfiguration.defaultConfig
        let config = AppConfiguration.emptyConfig

        let result = config |> Configuration.mergeDefaults defaultConfig

        result = defaultConfig

    [<Property>]
    let ``mergeDefaults merges default values into config with config higher order`` (config: AppConfiguration) =
        let defaultConfig = AppConfiguration.defaultConfig
        let result = config |> Configuration.mergeDefaults defaultConfig

        let apply (left: string) (right: string) =
            if String.IsNullOrWhiteSpace left then right else left

        let expected =
            { AppConfiguration.hostUrls = apply config.hostUrls defaultConfig.hostUrls
              allowExternalTraffic = apply config.allowExternalTraffic defaultConfig.allowExternalTraffic
              zkbRedisqBaseUrl = apply config.zkbRedisqBaseUrl defaultConfig.zkbRedisqBaseUrl
              zkbRedisqQueueId = apply config.zkbRedisqQueueId defaultConfig.zkbRedisqQueueId
              zkbRedisqTtwExternal = apply config.zkbRedisqTtwExternal defaultConfig.zkbRedisqTtwExternal
              zkbRedisqTtwClient = apply config.zkbRedisqTtwClient defaultConfig.zkbRedisqTtwClient
              zkbApiUrl = apply config.zkbApiUrl defaultConfig.zkbApiUrl
              evewhoApiUrl = apply config.evewhoApiUrl defaultConfig.evewhoApiUrl
              redisqSessionMaxAge = apply config.redisqSessionMaxAge defaultConfig.redisqSessionMaxAge
              killmailMemoryCacheAge = apply config.killmailMemoryCacheAge defaultConfig.killmailMemoryCacheAge
              mongoDbName = apply config.mongoDbName defaultConfig.mongoDbName
              mongoConnection = apply config.mongoConnection defaultConfig.mongoConnection }

        result = expected


    [<Property(MaxTest = 1)>]
    let ``validationErrors - empty returns errors`` () =
        let config = AppConfiguration.emptyConfig
        let r = Configuration.validationErrors config |> Array.ofSeq

        r <> Array.empty


    [<Property(MaxTest = 1)>]
    let ``validationErrors - default returns errors`` () =
        let config = AppConfiguration.defaultConfig
        let r = Configuration.validationErrors config |> Array.ofSeq

        r <> Array.empty

    [<Property(Arbitrary = [| typeof<AlphaNumericString> |], Verbose = true)>]
    let ``validationErrors - default with mongo details returns no errors`` (mongoConnection: string) =

        let config =
            { AppConfiguration.defaultConfig with
                mongoConnection = mongoConnection }

        let r = Configuration.validationErrors config |> Array.ofSeq

        r = Array.empty

    [<Property(Arbitrary = [| typeof<NullEmptyWhitespaceString> |], Verbose = true)>]
    let ``validationErrors - invalid hostUrls returns errors`` (hostUrls: string) =
        let config =
            { minimumValidConfig with
                hostUrls = hostUrls }

        let r = Configuration.validationErrors config |> Array.ofSeq

        r <> Array.empty && r |> Array.exists (fun s -> s.IndexOf("hostUrls") >= 0)

    [<Property(MaxTest = 1)>]
    let ``validate - minimumValidConfig throws no exception`` () =
        minimumValidConfig |> Configuration.validate |> ignore
        true

    [<Property(MaxTest = 1)>]
    let ``validate - empty config throws exception`` () =
        try
            AppConfiguration.emptyConfig |> Configuration.validate |> ignore
            false
        with ex ->
            true


    [<Property(Arbitrary = [| typeof<AlphaNumericString> |], Verbose = true)>]
    let ``applyKeyValues - apply zkbRedisqQueueId from key values`` (value: string) =
        let kvp = new MemoryKeyValueProvider() :> IKeyValueProvider
        kvp.SetValue "zkbRedisqQueueId" value

        let config = AppConfiguration.emptyConfig |> Configuration.applyKeyValues kvp

        config.zkbRedisqQueueId = value


    [<Property(Arbitrary = [| typeof<AlphaNumericString> |], Verbose = true)>]
    let ``applyKeyValues - zkbRedisqQueueId is set as a key value`` (value: string) =
        let kvp = new MemoryKeyValueProvider() :> IKeyValueProvider

        let config =
            { AppConfiguration.emptyConfig with
                zkbRedisqQueueId = value }
            |> Configuration.applyKeyValues kvp

        let persistedValue = kvp.GetValue "zkbRedisqQueueId"

        persistedValue = (Some value)
