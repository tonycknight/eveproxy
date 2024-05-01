namespace eveproxy.zkb

open System
open eveproxy
open Giraffe
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection

module ApiStartup =
    let addServices (sc: IServiceCollection) =
        sc
            .AddSingleton<IZkbStatsActor, ZkbStatsActor>()
            .AddSingleton<ISessionsActor, SessionsActor>()
            .AddSingleton<IRedisqIngestionActor, RedisqIngestionActor>()
            .AddSingleton<IKillmailRepository, MongoKillmailRepository>()
            .AddSingleton<IKillmailReferenceQueueFinder, MongoKillmailReferenceQueueFinder>()
            .AddSingleton<IKillmailReferenceQueueFactory, KillmailReferenceQueueFactory<MongoKillmailReferenceQueue>>()
            .AddSingleton<IKillmailWriter, KillmailWriter>()
            .AddSingleton<IKillmailReader, KillmailReader>()
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
        let stats = ctx.GetService<IZkbStatsActor>()

        { DistributedKills.count = 1
          session = sessionId }
        :> obj
        |> ActorMessage.Entity
        |> stats.Post

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

    let private getZkbApiRoute (routePrefix: string) (request: HttpRequest) =
        let path = request.Path

        let path =
            match path.HasValue with
            | true -> Some path.Value
            | _ -> None

        path
        |> Option.map (fun p -> $"{p.Substring(routePrefix.Length)}{request.QueryString}" |> Strings.trim)


    let private getZkbApi (routePrefix: string) =
        fun (next: HttpFunc) (ctx: HttpContext) ->
            task {
                let route = ctx.Request |> getZkbApiRoute routePrefix
                let notFound = RequestErrors.notFound (text "")
                let badRequest = RequestErrors.badRequest (text "")

                let! result =
                    match route with
                    | None -> task { return notFound }
                    | Some route when route = "" -> task { return notFound }
                    | Some route ->
                        task {
                            let! resp = ctx.GetService<IZkbApiPassthroughActor>().Get route

                            return
                                match resp with
                                | HttpOkRequestResponse(_, body, mediaType, _) ->
                                    match mediaType with
                                    | Some mt -> body |> eveproxy.Api.contentString mt
                                    | _ -> body |> eveproxy.Api.jsonString
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


    let redisqWebRoutes () =
        subRouteCi
            "/redisq"
            (GET
             >=> ResponseCaching.noResponseCaching
             >=> (setContentType "application/json")
             >=> choose
                     [ subRouteCi
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
             >=> ResponseCaching.noResponseCaching
             >=> (setContentType "application/json")
             >=> choose [ subRouteCi "/v1" (choose [ routeStartsWithCi "/" >=> (getZkbApi "/api/zkb/v1/") ]) ])
