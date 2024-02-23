namespace eveproxy.evewho

open System
open eveproxy
open Giraffe
open Microsoft.AspNetCore.Http

module Api =

    let private getEvewhoApiRoute (routePrefix: string) (request: HttpRequest) =
        let path = request.Path

        let path =
            match path.HasValue with
            | true -> Some path.Value
            | _ -> None

        path
        |> Option.map (fun p -> $"{p.Substring(routePrefix.Length)}{request.QueryString}" |> Strings.trim)

    let private countRouteFetch: HttpHandler =
        fun (next: HttpFunc) (ctx: HttpContext) ->

            // TODO: 

            next ctx

    let private getEvewhoApi (routePrefix: string) =
        fun (next: HttpFunc) (ctx: HttpContext) ->
            task {
                let route = ctx.Request |> getEvewhoApiRoute routePrefix
                
                return! Successful.ok (text "WIP") next ctx
            }

    let evewhoWebRoutes () =
        subRouteCi
            "/evewho"
            (GET
             >=> countRouteFetch
             >=> ResponseCaching.noResponseCaching
             >=> (setContentType "application/json")
             >=> choose [ subRouteCi "/v1" (choose [ routeStartsWithCi "/" >=> (getEvewhoApi "/api/evewho/v1/") ]) ])


