namespace eveproxy.zkb

open System
open eveproxy
open Giraffe
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection

module ApiStartup =
    let addServices (sc: IServiceCollection) =
        sc
            .AddSingleton<IApiStatsActor, ApiStatsActor>()
            .AddSingleton<ISessionsActor, SessionsActor>()
            .AddSingleton<IRedisqIngestionActor, RedisqIngestionActor>()
            .AddSingleton<IKillmailRepository, MongoKillmailRepository>()
            .AddSingleton<IKillmailReferenceQueueFactory, KillmailReferenceQueueFactory<MongoKillmailReferenceQueue>>()
            .AddSingleton<IKillmailWriter, KillmailWriter>()
            .AddSingleton<IKillmailReader, KillmailReader>()
            .AddSingleton<IKillWriteActor, KillWriteActor>()


    let start (sp: IServiceProvider) =
        let config = sp.GetRequiredService<AppConfiguration>()
        let ingest = sp.GetRequiredService<IRedisqIngestionActor>()

        config.ZkbRedisqUrl() |> ActorMessage.Pull |> ingest.Post


module Api =
    let private ttw (config: AppConfiguration) (query: IQueryCollection) =
        match query.TryGetValue("ttw") with
        | true, x -> x |> Seq.head |> Strings.toInt (config.ClientRedisqTtw())
        | _ -> config.ClientRedisqTtw()

    let private countSessionKillFetch (ctx: HttpContext) sessionId =
        let stats = ctx.GetService<IApiStatsActor>()

        { DistributedKills.count = 1
          session = sessionId }
        :> obj
        |> ActorMessage.Entity
        |> stats.Post


    let private countRouteFetch: HttpHandler =
        fun (next: HttpFunc) (ctx: HttpContext) ->

            if ctx.Request.Path.HasValue then
                let stats = ctx.GetService<IApiStatsActor>()
                ActorMessage.RouteFetch(ctx.Request.Path.Value, 1) |> stats.Post

            next ctx

    let private getNullKill =
        fun (next: HttpFunc) (ctx: HttpContext) -> task { return! Successful.OK KillPackage.empty next ctx }

    let private getNextKill sessionId =
        let rec pollPackage (sessions: ISessionsActor) (time: ITimeProvider) (endTime) =
            task {
                let! package = sessions.GetNext sessionId

                return!
                    if package <> KillPackage.empty then
                        task { return package }
                    else if time.GetUtcNow() >= endTime then
                        task { return KillPackage.empty }
                    else
                        task {
                            do! Threading.Tasks.Task.Delay(100)

                            return! endTime |> pollPackage sessions time
                        }
            }

        fun (next: HttpFunc) (ctx: HttpContext) ->
            task {
                let sessions = ctx.GetService<ISessionsActor>()
                let time = ctx.GetService<ITimeProvider>()
                let config = ctx.GetService<AppConfiguration>()

                let! package =
                    ctx.Request.Query
                    |> ttw config
                    |> time.GetUtcNow().AddSeconds
                    |> pollPackage sessions time

                if package <> KillPackage.empty then
                    sessionId |> countSessionKillFetch ctx

                return! Successful.OK package next ctx
            }

    let private getKillById killId =
        fun (next: HttpFunc) (ctx: HttpContext) ->
            task {
                let kms = ctx.GetService<IKillmailReader>()

                let! km = kms.ReadAsync killId

                return!
                    match km with
                    | Some km -> Successful.OK km next ctx
                    | _ -> RequestErrors.notFound (text "") next ctx
            }

    let private getRediqStats =

        fun (next: HttpFunc) (ctx: HttpContext) ->
            task {
                let kmRepo = ctx.GetService<IKillmailRepository>()
                let statsActor = ctx.GetService<IApiStatsActor>()

                let! statsActorStats = statsActor.GetStats()
                let! apiStats = statsActor.GetApiStats()

                let! kmCount = kmRepo.GetCountAsync()

                let! ingestActorStats = ctx.GetService<IRedisqIngestionActor>().GetStats()
                let! writeActorStats = ctx.GetService<IKillWriteActor>().GetStats()
                let! sessionsActorStats = ctx.GetService<ISessionsActor>().GetStats()

                let result =
                    {| actors = [| statsActorStats; ingestActorStats; writeActorStats; sessionsActorStats |]
                       stats =
                        {| ingestion = apiStats.ingestion
                           distribution = apiStats.distribution
                           storage = {| kills = kmCount |}
                           routes = apiStats.routes |> Map.values |> Seq.sortByDescending (fun rs -> rs.count) |} |}

                return! Successful.OK result next ctx
            }

    let private getZkbStats =

        fun (next: HttpFunc) (ctx: HttpContext) ->
            task {
                let statsActor = ctx.GetService<IApiStatsActor>()

                let! statsActorStats = statsActor.GetStats()
                let! apiStats = statsActor.GetApiStats()

                let result =
                    {| actors = [| statsActorStats |]
                       routes = apiStats.routes |> Map.values |> Seq.sortByDescending (fun rs -> rs.count) |}

                return! Successful.OK result next ctx
            }

    let redisqWebRoutes () =
        subRouteCi
            "/redisq"
            (GET
             >=> countRouteFetch
             >=> ResponseCaching.noResponseCaching
             >=> (setContentType "application/json")
             >=> choose
                     [ route "/stats/" >=> getRediqStats
                       subRouteCi
                           "/v1"
                           (choose
                               [ routeCif "/kills/session/%s/" (fun session -> getNextKill session)
                                 routeCif "/kills/id/%s/" (fun killId -> getKillById killId)
                                 route "/kills/null/" >=> getNullKill
                                 route "/kills/replay/" >=> getNullKill
                                 route "/kills/" >=> (getNextKill "") ]) ])

    let zkbWebRoutes () =
        subRouteCi
            "/zkb"
            (GET
             >=> countRouteFetch
             >=> ResponseCaching.noResponseCaching
             >=> (setContentType "application/json")
             >=> choose [ route "/stats/" >=> getZkbStats ])
