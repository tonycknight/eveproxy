﻿namespace eveproxy

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
        authorizeRequest (isValidApiKey keyValues >||> isHomeIp) accessDenied

    let isAuthorised (sp: System.IServiceProvider) : HttpHandler =
        let keyValues = sp.GetRequiredService<IKeyValueProvider>()
        requiresValidIp >=> requiresApiKey keyValues
