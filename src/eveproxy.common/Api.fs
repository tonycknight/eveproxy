namespace eveproxy

open System.Net
open Giraffe
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection

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
        authorizeRequest (isValidApiKey keyValues ||>> isHomeIp) accessDenied

    let isAuthorised (sp: System.IServiceProvider) : HttpHandler =
        let keyValues = sp.GetRequiredService<IKeyValueProvider>()
        requiresValidIp >=> requiresApiKey keyValues

    let contentString (contentType: string) (value: string) : HttpHandler =
        let bytes = System.Text.Encoding.UTF8.GetBytes value

        fun (_: HttpFunc) (ctx: HttpContext) ->
            ctx.SetContentType contentType
            ctx.WriteBytesAsync bytes

    let jsonString (value: string) =
        value |> contentString "application/json; charset=utf-8"

    let countRouteFetch: HttpHandler =
        fun (next: HttpFunc) (ctx: HttpContext) ->

            if ctx.Request.Path.HasValue then
                let stats = ctx.GetService<IStatsActor>()
                let route = ctx.Request.Path.Value |> Strings.toLower
                ActorMessage.RouteFetch(route, 1) |> stats.Post

            next ctx
