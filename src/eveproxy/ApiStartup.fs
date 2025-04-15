namespace eveproxy

open System
open Giraffe
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open OpenTelemetry.Exporter
open OpenTelemetry.Metrics
open OpenTelemetry.Resources

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

    let addOpenTelemetry (services: IServiceCollection) =
        let config = services.BuildServiceProvider().GetRequiredService<AppConfiguration>()

        let resourceBuilder =
            ResourceBuilder.CreateDefault().AddService(config.otelServiceName)

        let otlpOptions (otlp: OtlpExporterOptions) (metrics: MetricReaderOptions) =
            otlp.Endpoint <- config.otelCollectorUrl |> Strings.appendIfMissing "/" |> Uri
            metrics.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds <- 15000
            metrics.TemporalityPreference <- MetricReaderTemporalityPreference.Cumulative
            metrics |> ignore

        services
            .AddOpenTelemetry()
            .WithMetrics(fun opts ->
                opts
                    .SetResourceBuilder(resourceBuilder)
                    .AddMeter("eveproxy_killmails")
                    .AddMeter("eveproxy_request_esi")
                    .AddMeter("eveproxy_cache_esi")
                    .AddMeter("eveproxy_request_evewho")
                    .AddMeter("eveproxy_request_zkb")
                    .AddMeter("eveproxy_proxy_request")
                    .AddOtlpExporter(otlpOptions)
                |> ignore)
        |> ignore

        services.AddSingleton<IMetricsTelemetry, MetricsTelemetry>()

    let addApiConfig (services: IServiceCollection) =
        let sp = services.BuildServiceProvider()
        let lf = sp.GetRequiredService<ILoggerFactory>()

        let config = Configuration.create sp
        let kvs = eveproxy.MongoKeyValueProvider(lf, config) :> eveproxy.IKeyValueProvider

        let config = config |> Configuration.applyKeyValues kvs

        services.AddSingleton<AppConfiguration>(config).AddSingleton<eveproxy.IKeyValueProvider>(kvs)

    let addApiHttp (services: IServiceCollection) =
        services.AddHttpClient().AddSingleton<IExternalHttpClient, ExternalHttpClient>()

    let addWebFramework (services: IServiceCollection) = services.AddGiraffe()

    let addContentNegotiation (services: IServiceCollection) =
        services.AddSingleton<INegotiationConfig, JsonOnlyNegotiationConfig>()

    let addApi<'a when 'a :> IServiceCollection> =
        addCommonInfrastructure
        >> addApiLogging
        >> addApiConfig
        >> addOpenTelemetry
        >> addApiHttp
        >> addWebFramework
        >> addContentNegotiation

    let configSource (args: string[]) (whbc: IConfigurationBuilder) =
        let whbc =
            whbc.AddJsonFile("appsettings.json", true, false).AddEnvironmentVariables("eveproxy_").AddCommandLine(args)

        let configPath =
            args
            |> Args.getValue "--config="
            |> Option.map (Io.toFullPath >> Io.normalise)
            |> Option.defaultValue ""

        if configPath <> "" then
            if Io.fileExists configPath |> not then
                failwithf $"{configPath} not found."

            whbc.AddJsonFile(configPath, true, false)
        else
            whbc
