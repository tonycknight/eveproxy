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
            .AddSingleton<IKillmailReferenceQueueFinder, MongoKillmailReferenceQueueFinder>()
            .AddSingleton<IKillmailReferenceQueueFactory, KillmailReferenceQueueFactory<MongoKillmailReferenceQueue>>()
            .AddSingleton<IKillmailWriter, KillmailWriter>()
            .AddSingleton<IKillmailReader, KillmailReader>()
            .AddSingleton<IKillWriteActor, KillWriteActor>()
            .AddSingleton<IZkbApiPassthroughActor, ZkbApiPassthroughActor>()


    let start (sp: IServiceProvider) =
        let config = sp.GetRequiredService<AppConfiguration>()
        let ingest = sp.GetRequiredService<IRedisqIngestionActor>()

        config.ZkbRedisqUrl() |> ActorMessage.Pull |> ingest.Post

[<CLIMutable>]
type KillPackage =
    { package: obj }

    static member ofKillPackageData(value: KillPackageData) = { KillPackage.package = value.package }

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
                let route = ctx.Request.Path.Value |> Strings.toLower
                ActorMessage.RouteFetch(route, 1) |> stats.Post

            next ctx

    let private getNullKill =
        fun (next: HttpFunc) (ctx: HttpContext) ->
            task { return! Successful.OK (KillPackageData.empty |> KillPackage.ofKillPackageData) next ctx }

    let private getNextKill sessionId =
        let rec pollPackage (sessions: ISessionsActor) (time: ITimeProvider) (endTime) =
            task {
                let! package = sessions.GetNext sessionId

                return!
                    if package <> KillPackageData.empty then
                        task { return package }
                    else if time.GetUtcNow() >= endTime then
                        task { return KillPackageData.empty }
                    else
                        task {
                            do! Threading.Tasks.Task.Delay(100)

                            return! endTime |> pollPackage sessions time
                        }
            }

        fun (next: HttpFunc) (ctx: HttpContext) ->
            task {
                let sessionId = sessionId |> Strings.toLower
                let sessions = ctx.GetService<ISessionsActor>()
                let time = ctx.GetService<ITimeProvider>()
                let config = ctx.GetService<AppConfiguration>()

                let! package =
                    ctx.Request.Query
                    |> ttw config
                    |> time.GetUtcNow().AddSeconds
                    |> pollPackage sessions time

                if package <> KillPackageData.empty then
                    sessionId |> countSessionKillFetch ctx

                return! Successful.OK (package |> KillPackage.ofKillPackageData) next ctx
            }

    let private getKillById killId =
        fun (next: HttpFunc) (ctx: HttpContext) ->
            task {
                let kms = ctx.GetService<IKillmailReader>()

                let! km = kms.ReadAsync killId

                return!
                    match km with
                    | Some km -> Successful.OK (km |> KillPackage.ofKillPackageData) next ctx
                    | _ -> RequestErrors.notFound (text "") next ctx
            }

    let private getRediqStats =

        fun (next: HttpFunc) (ctx: HttpContext) ->
            task {
                let kmRepo = ctx.GetService<IKillmailRepository>()
                let statsActor = ctx.GetService<IApiStatsActor>()
                let sessionsActor = ctx.GetService<ISessionsActor>()

                let! statsActorStats = statsActor.GetStats()
                let! apiStats = statsActor.GetApiStats()

                let! kmCount = kmRepo.GetCountAsync()

                let! ingestActorStats = ctx.GetService<IRedisqIngestionActor>().GetStats()
                let! writeActorStats = ctx.GetService<IKillWriteActor>().GetStats()
                let! sessionsActorStats = sessionsActor.GetStats()
                let! sessionStorageStats = sessionsActor.GetStorageStats()

                let result =
                    {| actors = [| statsActorStats; ingestActorStats; writeActorStats; sessionsActorStats |]
                       stats =
                        {| ingestion = apiStats.ingestion
                           distribution = apiStats.distribution
                           storage =
                            {| kills = kmCount
                               sessions = sessionStorageStats |}
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

    let private getZkbApi (routePrefix: string) =
        fun (next: HttpFunc) (ctx: HttpContext) ->
            task {

                let path = ctx.Request.Path

                let route =
                    match path.HasValue with
                    | true ->
                        $"{path.Value.Substring(routePrefix.Length)}{ctx.Request.QueryString}"
                        |> Strings.trim
                        |> Some
                    | false -> None
                                    
                let! result =
                    match route with
                    | None -> task { return RequestErrors.notFound (text "") }
                    | Some r when r = "" -> task { return RequestErrors.notFound (text "") }
                    | Some r ->                         
                        task {
                            let! resp = ctx.GetService<IZkbApiPassthroughActor>().Get r
                            
                            return
                                match resp with
                                | HttpOkRequestResponse(_, body) ->
                                    // TODO: Hack to work around Giraffe's automatic Json encoding....
                                    Newtonsoft.Json.JsonConvert.DeserializeObject(body) |> Successful.OK
                                | HttpTooManyRequestsResponse _ -> RequestErrors.tooManyRequests (text "")
                                | HttpExceptionRequestResponse _ -> ServerErrors.internalError (text "")
                                | HttpErrorRequestResponse(rc,_) when rc = System.Net.HttpStatusCode.NotFound -> RequestErrors.notFound (text "")
                                | HttpErrorRequestResponse(rc,_) when rc = System.Net.HttpStatusCode.BadRequest -> RequestErrors.badRequest (text "")
                                | _ -> RequestErrors.notFound (text "")
                        }

                return! result next ctx
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
                                 route "/kills/" >=> (getNextKill KillmailReferenceQueues.defaultQueueName) ]) ])

    let zkbWebRoutes () =
        subRouteCi
            "/zkb"
            (GET
             >=> countRouteFetch
             >=> ResponseCaching.noResponseCaching
             >=> (setContentType "application/json")
             >=> choose
                     [ route "/stats/" >=> getZkbStats
                       subRouteCi "/v1" (choose [ routeStartsWithCi "/" >=> (getZkbApi "/api/zkb/v1/") ]) ])
