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

    let private getEvewhoApi (routePrefix: string) =
        fun (next: HttpFunc) (ctx: HttpContext) ->
            task {
                let route = ctx.Request |> getEvewhoApiRoute routePrefix
                let notFound = RequestErrors.notFound (text "")
                let badRequest = RequestErrors.badRequest (text "")

                let! result =
                    match route with
                    | None -> task { return notFound }
                    | Some route when route = "" -> task { return notFound }
                    | Some route ->
                        task {
                            let! resp = ctx.GetService<IEvewhoApiPassthroughActor>().Get route

                            return
                                match resp with
                                | HttpOkRequestResponse(_, body, mediaType) ->
                                    match mediaType with
                                    | Some mt -> body |> eveproxy.Api.contentString mt
                                    | _ -> body |> eveproxy.Api.jsonString
                                | HttpTooManyRequestsResponse _ -> RequestErrors.tooManyRequests (text "")
                                | HttpExceptionRequestResponse _ -> ServerErrors.internalError (text "")
                                | HttpErrorRequestResponse(rc, _) when rc = System.Net.HttpStatusCode.NotFound ->
                                    notFound
                                | HttpErrorRequestResponse(rc, _) when rc = System.Net.HttpStatusCode.BadRequest ->
                                    badRequest
                                | HttpErrorRequestResponse(rc, _) when
                                    rc = System.Net.HttpStatusCode.InternalServerError
                                    ->
                                    ServerErrors.internalError (text "")
                                | _ -> badRequest
                        }

                return! result next ctx
            }

    let evewhoWebRoutes () =
        subRouteCi
            "/evewho"
            (GET
             >=> ResponseCaching.noResponseCaching
             >=> (setContentType "application/json")
             >=> choose [ subRouteCi "/v1" (choose [ routeStartsWithCi "/" >=> (getEvewhoApi "/api/evewho/v1/") ]) ])
