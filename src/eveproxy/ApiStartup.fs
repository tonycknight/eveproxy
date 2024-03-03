namespace eveproxy

open System
open Giraffe
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging

module ApiStartup =
    open Microsoft.AspNetCore.HttpLogging

    let addCommonInfrastructure (services: IServiceCollection) =
        services.AddSingleton<ITimeProvider, eveproxy.TimeProvider>().AddMemoryCache()

    let addApiLogging (services: IServiceCollection) =
        services
            .AddLogging()
            .AddHttpLogging(fun lo ->
                lo.LoggingFields <-
                    HttpLoggingFields.Duration
                    ||| HttpLoggingFields.RequestPath
                    ||| HttpLoggingFields.RequestQuery
                    ||| HttpLoggingFields.RequestProtocol
                    ||| HttpLoggingFields.RequestMethod
                    ||| HttpLoggingFields.RequestScheme
                    ||| HttpLoggingFields.ResponseStatusCode

                lo.CombineLogs <- true)

    let addStats (services: IServiceCollection) =
        services.AddSingleton<IStatsActor, StatsActor>()

    let addApiConfig (services: IServiceCollection) =
        let sp = services.BuildServiceProvider()
        let lf = sp.GetRequiredService<ILoggerFactory>()

        let config = Configuration.create sp
        let kvs = eveproxy.MongoKeyValueProvider(lf, config) :> eveproxy.IKeyValueProvider

        let config = config |> Configuration.applyKeyValues kvs

        services
            .AddSingleton<AppConfiguration>(config)
            .AddSingleton<eveproxy.IKeyValueProvider>(kvs)

    let addApiHttp (services: IServiceCollection) =
        services.AddHttpClient().AddSingleton<IExternalHttpClient, ExternalHttpClient>()

    let addWebFramework (services: IServiceCollection) = services.AddGiraffe()

    let addContentNegotiation (services: IServiceCollection) =
        services.AddSingleton<INegotiationConfig, JsonOnlyNegotiationConfig>()

    let addApi<'a when 'a :> IServiceCollection> =
        addCommonInfrastructure
        >> addApiLogging
        >> addApiConfig
        >> addStats
        >> addApiHttp
        >> addWebFramework
        >> addContentNegotiation

    let configSource (args: string[]) (whbc: IConfigurationBuilder) =
        let whbc = 
            whbc
                .AddJsonFile("appsettings.json", true, false)
                .AddEnvironmentVariables("eveproxy_")
                .AddCommandLine(args)

        let configPath = args |> Args.getValue "--config=" |> Option.map (Io.toFullPath >> Io.normalise) |> Option.defaultValue ""
        
        if configPath <> "" then
            if Io.fileExists configPath |> not then
                failwithf $"{configPath} not found."
            whbc.AddJsonFile(configPath, true, false)
        else
            whbc
