namespace eveproxy

open System.Net
open Giraffe
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection

module Api =

    let contentString (contentType: string) (value: string) : HttpHandler =
        let bytes = System.Text.Encoding.UTF8.GetBytes value

        fun (_: HttpFunc) (ctx: HttpContext) ->
            ctx.SetContentType contentType
            ctx.WriteBytesAsync bytes

    let jsonString (value: string) =
        value |> contentString "application/json; charset=utf-8"

    let countRouteInvoke: HttpHandler =
        fun (next: HttpFunc) (ctx: HttpContext) ->

            if ctx.Request.Path.HasValue then
                let stats = ctx.GetService<IStatsActor>()
                let route = ctx.Request.Path.Value |> Strings.toLower
                ActorMessage.RouteFetch(ctx.Request.Method, route, 1) |> stats.Post

            next ctx
