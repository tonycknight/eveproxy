namespace eveproxy

open System
open System.Net
open Giraffe
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection

module Api =
    let private isHomeIp (ctx: HttpContext) =
        ctx.Connection.RemoteIpAddress |> IPAddress.IsLoopback

    let private isValidApiKey (secrets: ISecretProvider) (ctx: HttpContext) =
        match ctx.TryGetRequestHeader "x-api-key" with
        | Some k -> secrets.IsSecretValueEqual "apikey" k
        | None -> false

    let private accessDenied: HttpHandler = setStatusCode 401 >=> setBody [||]
    let private forbidden: HttpHandler = setStatusCode 403 >=> setBody [||]

    let private requiresValidIp: HttpHandler = authorizeRequest isHomeIp forbidden

    let private requiresApiKey secrets : HttpHandler =
        authorizeRequest (isValidApiKey secrets >||> isHomeIp) accessDenied

    let isAuthorised (sp: System.IServiceProvider) : HttpHandler =
        let secrets = sp.GetRequiredService<ISecretProvider>()
        requiresValidIp >=> requiresApiKey secrets

module ApiStartup =

    let addCommonInfrastructure (services: IServiceCollection) =
        services.AddSingleton<ITimeProvider, TimeProvider>()

    let addApiLogging (services: IServiceCollection) =
        services
            .AddLogging()
            .AddHttpLogging(fun lo -> lo.LoggingFields <- Microsoft.AspNetCore.HttpLogging.HttpLoggingFields.Request)

    let addApiConfig (services: IServiceCollection) =
        services
            .AddSingleton<AppConfiguration>(Configuration.create)
            .AddSingleton<eveproxy.ISecretProvider, eveproxy.StubSecretProvider>()

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
