namespace eveproxy.common.tests.unit.Utils

open System
open FsCheck
open FsCheck.Xunit
open eveproxy

module ConfigurationTests =


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
            { AppConfiguration.zkbRedisqBaseUrl = apply config.zkbRedisqBaseUrl defaultConfig.zkbRedisqBaseUrl
              zkbRedisqQueueId = apply config.zkbRedisqQueueId defaultConfig.zkbRedisqQueueId
              zkbRedisqTtwExternal = apply config.zkbRedisqTtwExternal defaultConfig.zkbRedisqTtwExternal
              zkbRedisqTtwClient = apply config.zkbRedisqTtwClient defaultConfig.zkbRedisqTtwClient
              zkbApiUrl = apply config.zkbApiUrl defaultConfig.zkbApiUrl
              redisqSessionMaxAge = apply config.redisqSessionMaxAge defaultConfig.redisqSessionMaxAge }

        result = expected
