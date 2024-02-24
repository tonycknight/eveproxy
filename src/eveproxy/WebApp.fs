namespace eveproxy

open System
open eveproxy.zkb
open eveproxy.evewho
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
                let statsActor = ctx.GetService<IStatsActor>()
                let zkbStatsActor = ctx.GetService<IZkbStatsActor>()
                let sessionsActor = ctx.GetService<ISessionsActor>()

                let! apiStats = statsActor.GetApiStats()

                let! zkbStatsActorStats = zkbStatsActor.GetStats()
                let! zkbApiStats = zkbStatsActor.GetApiStats()

                let! ingestActorStats = ctx.GetService<IRedisqIngestionActor>().GetStats()
                let! sessionsActorStats = sessionsActor.GetStats()
                let! zkbPassthruStats = ctx.GetService<IZkbApiPassthroughActor>().GetStats()
                let! evewhoPassthruStats = ctx.GetService<IEvewhoApiPassthroughActor>().GetStats()

                let! kmCount = ctx.GetService<IKillmailRepository>().GetCountAsync()
                let! sessionStorageStats = sessionsActor.GetStorageStats()

                let result =
                    {| actors =
                        [| zkbStatsActorStats
                           ingestActorStats
                           sessionsActorStats
                           zkbPassthruStats
                           evewhoPassthruStats |]
                       killmails =
                        {| ingestion = zkbApiStats.ingestion
                           distribution = zkbApiStats.distribution
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
                  >=> Api.countRouteFetch
                  >=> subRouteCi
                          "/api"
                          (choose
                              [ choose
                                    [ heartbeat
                                      route "/stats/" >=> stats
                                      eveproxy.zkb.Api.redisqWebRoutes ()
                                      eveproxy.zkb.Api.zkbWebRoutes ()
                                      eveproxy.evewho.Api.evewhoWebRoutes () ] ]) ]
