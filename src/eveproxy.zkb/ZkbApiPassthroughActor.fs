namespace eveproxy.zkb

open System
open eveproxy
open Microsoft.Extensions.Logging

type private ZkbApiPassthroughActorState =
    { throttling: Map<DateTime, int> }

    static member empty = { ZkbApiPassthroughActorState.throttling = Map.empty }

type ZkbApiPassthroughActor
    (hc: IExternalHttpClient, stats: IZkbStatsActor, logFactory: ILoggerFactory, config: AppConfiguration) =
    let log = logFactory.CreateLogger<ZkbApiPassthroughActor>()
    let throttle = config.ZkbThrottling () |> Throttling.windowThrottling

    let checkThrottling (counts: Map<DateTime, int>) =
        task {
            let (newCounts, wait) = throttle counts DateTime.UtcNow

            if wait > TimeSpan.Zero then
                $"Waiting {wait} before next request to Zkb" |> log.LogTrace

            do! System.Threading.Tasks.Task.Delay wait
            return newCounts
        }

    let rec getZkbApiIterate throttling count url =
        task {
            try
                let! newThrottling = checkThrottling throttling

                $"GET [{url}] iteration #{count}..." |> log.LogTrace
                let! resp = hc.GetAsync url

                $"GET {HttpRequestResponse.loggable resp} received from [{url}]."
                |> log.LogTrace

                return!
                    match resp with
                    | HttpTooManyRequestsResponse _ when count <= 0 -> (newThrottling, resp) |> Threading.toTaskResult
                    | HttpOkRequestResponse _
                    | HttpErrorRequestResponse _
                    | HttpExceptionRequestResponse _ -> (newThrottling, resp) |> Threading.toTaskResult
                    | HttpTooManyRequestsResponse _ -> getZkbApiIterate throttling (count - 1) url
            with ex ->
                log.LogError(ex.Message, ex)
                return (throttling, HttpErrorRequestResponse(Net.HttpStatusCode.InternalServerError, ""))
        }

    let getZkbApi throttling route =
        let url = $"{config.zkbApiUrl}{route}"
        getZkbApiIterate throttling 10 url

    let actor =
        MailboxProcessor<ActorMessage>.Start(fun inbox ->
            let rec loop (state: ZkbApiPassthroughActorState) =
                async {
                    let! msg = inbox.Receive()

                    let! state =
                        match msg with
                        | ActorMessage.PullReply(route, rc) ->
                            task {
                                let! (throttling, resp) = getZkbApi state.throttling route
                                (resp :> obj) |> rc.Reply

                                return { ZkbApiPassthroughActorState.throttling = throttling }
                            }
                            |> Async.AwaitTask
                        | _ -> async { return state }

                    return! loop state
                }

            ZkbApiPassthroughActorState.empty |> loop)

    interface IZkbApiPassthroughActor with
        member this.GetStats() =
            task {
                return
                    { ActorStats.name = (typedefof<ZkbApiPassthroughActor>).FullName
                      queueCount = actor.CurrentQueueLength
                      childStats = [] }
            }

        member this.Post(msg: ActorMessage) = actor.Post msg

        member this.Get(route: string) =
            task {
                let! r = actor.PostAndAsyncReply(fun rc -> ActorMessage.PullReply(route, rc))

                return (r :?> HttpRequestResponse)
            }
