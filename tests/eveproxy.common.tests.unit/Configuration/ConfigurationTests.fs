namespace eveproxy.common.tests.unit.Utils

open System
open eveproxy
open eveproxy.common.tests.unit
open FsCheck.Xunit

module ConfigurationTests =

    let minimumValidConfig =
        { AppConfiguration.defaultConfig with
            mongoUserName = "aaa"
            mongoPassword = "aaa" }

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
              redisqSessionMaxAge = apply config.redisqSessionMaxAge defaultConfig.redisqSessionMaxAge
              mongoServer = apply config.mongoServer defaultConfig.mongoServer
              mongoDbName = apply config.mongoDbName defaultConfig.mongoDbName
              mongoUserName = apply config.mongoUserName defaultConfig.mongoUserName
              mongoPassword = apply config.mongoPassword defaultConfig.mongoPassword }

        result = expected


    [<Property(MaxTest = 1)>]
    let ``validate on empty throws exception`` () =
        let config = AppConfiguration.emptyConfig

        try
            Configuration.validate config |> ignore
            false
        with ex ->
            true



    [<Property(MaxTest = 1)>]
    let ``validate on default throws exception`` () =
        let config = AppConfiguration.defaultConfig

        try
            Configuration.validate config |> ignore
            false
        with ex ->
            true

    [<Property(Arbitrary = [| typeof<AlphaNumericString> |], Verbose = true)>]
    let ``validate on default with mongo details throws no exception`` (mongoUserName: string) (mongoPassword: string) =

        let config =
            { AppConfiguration.defaultConfig with
                mongoUserName = mongoUserName
                mongoPassword = mongoPassword }

        Configuration.validate config |> ignore

        true

    [<Property(Arbitrary = [| typeof<NullEmptyWhitespaceString> |], Verbose = true)>]
    let ``validate - invalid hostUrls throws exception`` (hostUrls: string) =
        let config =
            { minimumValidConfig with
                hostUrls = hostUrls }

        try
            Configuration.validate config |> ignore
            false
        with ex ->
            true
