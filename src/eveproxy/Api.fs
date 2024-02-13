namespace eveproxy

open System
open System.Net
open Giraffe
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging

module Api =
    let private isHomeIp (ctx: HttpContext) =
        let config = ctx.GetService<AppConfiguration>()

        match config.allowExternalTraffic |> Strings.toBool false with
        | true -> true
        | _ -> ctx.Connection.RemoteIpAddress |> IPAddress.IsLoopback

    let private isValidApiKey (keyValues: IKeyValueProvider) (ctx: HttpContext) =
        match ctx.TryGetRequestHeader "x-api-key" with
        | Some k -> keyValues.IsValueEqual "apikey" k
        | None -> false

    let private accessDenied: HttpHandler = setStatusCode 401 >=> setBody [||]
    let private forbidden: HttpHandler = setStatusCode 403 >=> setBody [||]

    let private requiresValidIp: HttpHandler = authorizeRequest isHomeIp forbidden

    let private requiresApiKey keyValues : HttpHandler =
        authorizeRequest (isValidApiKey keyValues >||> isHomeIp) accessDenied

    let isAuthorised (sp: System.IServiceProvider) : HttpHandler =
        let keyValues = sp.GetRequiredService<IKeyValueProvider>()
        requiresValidIp >=> requiresApiKey keyValues

module ApiStartup =

    let addCommonInfrastructure (services: IServiceCollection) =
        services.AddSingleton<ITimeProvider, eveproxy.TimeProvider>()

    let addApiLogging (services: IServiceCollection) =
        services
            .AddLogging()
            .AddHttpLogging(fun lo -> lo.LoggingFields <- Microsoft.AspNetCore.HttpLogging.HttpLoggingFields.Request)

    let addApiConfig (services: IServiceCollection) =
        services
            .AddSingleton<eveproxy.IKeyValueProvider, eveproxy.MemoryKeyValueProvider>()
            .AddSingleton<AppConfiguration>(Configuration.create)
            
            
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
