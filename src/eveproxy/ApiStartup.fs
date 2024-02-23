﻿namespace eveproxy

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
        >> addApiHttp
        >> addWebFramework
        >> addContentNegotiation

    let configSource (args: string[]) (whbc: IConfigurationBuilder) =
        whbc
            .AddJsonFile("appsettings.json", true, false)
            .AddEnvironmentVariables("eveproxy_")
            .AddCommandLine(args)
