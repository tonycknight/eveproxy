namespace eveproxy.esi

open System
open eveproxy
open Giraffe
open Microsoft.AspNetCore.Http

module Api =

    let private transferredHeaders =
        let allowedHeaders =
            [| "x-esi-error-limit-remain"
               "x-esi-error-limit-reset"
               "last-modified"
               "expires"
               "etag" |]

        eveproxy.Api.pickHeaders allowedHeaders

    let private getEsiApiRoute (routePrefix: string) (request: HttpRequest) =
        let path = request.Path

        let path =
            match path.HasValue with
            | true -> Some path.Value
            | _ -> None

        path
        |> Option.map (fun p -> $"{p.Substring(routePrefix.Length)}{request.QueryString}" |> Strings.trim)

    let private getEsiApi (routePrefix: string) =
        fun (next: HttpFunc) (ctx: HttpContext) ->
            task {
                let route = ctx.Request |> getEsiApiRoute routePrefix
                let notFound = RequestErrors.notFound (text "")
                let badRequest = RequestErrors.badRequest (text "")

                let! result =
                    match route with
                    | None -> task { return notFound }
                    | Some route when route = "" -> task { return notFound }
                    | Some route ->
                        task {
                            let! resp = ctx.GetService<EsiApiProxy>().Get route

                            return
                                match resp with
                                | HttpOkRequestResponse(_, body, mediaType, headers) ->
                                    match mediaType with
                                    | Some mt -> body |> eveproxy.Api.contentString mt (transferredHeaders headers)
                                    | _ -> body |> eveproxy.Api.jsonString (transferredHeaders headers)
                                | HttpTooManyRequestsResponse _ -> RequestErrors.tooManyRequests (text "")
                                | HttpExceptionRequestResponse _ -> ServerErrors.internalError (text "")
                                | HttpErrorRequestResponse(rc, _, _) when rc = System.Net.HttpStatusCode.NotFound ->
                                    notFound
                                | HttpErrorRequestResponse(rc, _, _) when rc = System.Net.HttpStatusCode.BadRequest ->
                                    badRequest
                                | HttpErrorRequestResponse(rc, _, _) when
                                    rc = System.Net.HttpStatusCode.InternalServerError
                                    ->
                                    ServerErrors.internalError (text "")
                                | _ -> badRequest
                        }

                return! result next ctx
            }

    let esiWebRoutes () =
        subRouteCi
            "/esi"
            (GET
             >=> (ApiTelemetry.countRouteInvoke (fun m -> m.EsiProxyRequest 1))
             >=> ResponseCaching.noResponseCaching
             >=> (setContentType "application/json")
             >=> choose [ subRouteCi "/v1" (choose [ routeStartsWithCi "/" >=> (getEsiApi "/api/esi/v1/") ]) ])
