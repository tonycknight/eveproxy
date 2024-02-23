namespace eveproxy

open System
open eveproxy.zkb
open Giraffe
open Microsoft.AspNetCore.Http

module WebApp =

    let favicon =
        GET
        >=> route "/favicon.ico"
        >=> ResponseCaching.publicResponseCaching 999999 None
        >=> Successful.NO_CONTENT

    let private heartbeat =
        GET
        >=> route "/heartbeat/"
        >=> ResponseCaching.noResponseCaching
        >=> json [ "OK" ]

    let private stats =

        fun (next: HttpFunc) (ctx: HttpContext) ->
            task {
                let statsActor = ctx.GetService<IApiStatsActor>()
                let sessionsActor = ctx.GetService<ISessionsActor>()

                let! statsActorStats = statsActor.GetStats()
                let! apiStats = statsActor.GetApiStats()

                let! ingestActorStats = ctx.GetService<IRedisqIngestionActor>().GetStats()
                let! sessionsActorStats = sessionsActor.GetStats()
                let! passthruStats = ctx.GetService<IZkbApiPassthroughActor>().GetStats()

                let! kmCount = ctx.GetService<IKillmailRepository>().GetCountAsync()
                let! sessionStorageStats = sessionsActor.GetStorageStats()

                let result =
                    {| actors = [| statsActorStats; ingestActorStats; sessionsActorStats; passthruStats |]
                       killmails =
                        {| ingestion = apiStats.ingestion
                           distribution = apiStats.distribution
                           storage =
                            {| kills = kmCount
                               sessions = sessionStorageStats |} |}
                       routes = apiStats.routes |> Map.values |> Seq.sortByDescending (fun rs -> rs.count) |}

                return! Successful.OK result next ctx
            }

    let webApp (sp: IServiceProvider) =
        Api.isAuthorised sp
        >=> choose
                [ favicon
                  GET
                  >=> subRouteCi
                          "/api"
                          (choose
                              [ choose
                                    [ heartbeat
                                      route "/stats/" >=> stats
                                      eveproxy.zkb.Api.redisqWebRoutes ()
                                      eveproxy.zkb.Api.zkbWebRoutes () 
                                      eveproxy.evewho.Api.evewhoWebRoutes ()
                                      ] ]) ]
