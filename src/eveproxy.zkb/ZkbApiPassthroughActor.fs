namespace eveproxy.zkb

open System
open eveproxy
open Microsoft.Extensions.Logging

type private ZkbApiPassthroughActorState =
    { lastZkbRequest: System.DateTime }

    static member empty =
        { ZkbApiPassthroughActorState.lastZkbRequest = System.DateTime.MinValue }

type ZkbApiPassthroughActor
    (hc: IExternalHttpClient, stats: IApiStatsActor, logFactory: ILoggerFactory, config: AppConfiguration) =
    let log = logFactory.CreateLogger<ZkbApiPassthroughActor>()

    let pause (lastPoll: DateTime) =
        task {
            let limit = TimeSpan.FromSeconds(1.)
            let diff = DateTime.UtcNow - lastPoll

            let duration =
                if diff < limit then limit
                else if diff > limit then TimeSpan.Zero
                else diff

            if duration > TimeSpan.Zero then
                log.LogTrace $"Waiting {duration} for Zkb API..."
                do! System.Threading.Tasks.Task.Delay duration
        }


    let rec getZkbApiIterate lastPoll count url =
        task {
            try
                do! pause lastPoll

                $"GET [{url}] iteration #{count}..." |> log.LogTrace
                let! resp = hc.GetAsync url
                $"GET {HttpRequestResponse.loggable resp} received from [{url}]." |> log.LogTrace

                return!
                    match resp with
                    | HttpTooManyRequestsResponse _ when count <= 0 -> resp |> Threading.toTaskResult
                    | HttpOkRequestResponse _
                    | HttpErrorRequestResponse _
                    | HttpExceptionRequestResponse _ -> resp |> Threading.toTaskResult
                    | HttpTooManyRequestsResponse _ -> getZkbApiIterate lastPoll (count - 1) url
            with ex ->
                log.LogError(ex.Message, ex)
                return HttpErrorRequestResponse(Net.HttpStatusCode.InternalServerError, "")
        }

    let getZkbApi lastPoll route =
        let url = $"{config.zkbApiUrl}{route}"
        getZkbApiIterate lastPoll 10 url

    let actor =
        MailboxProcessor<ActorMessage>.Start(fun inbox ->
            let rec loop (state: ZkbApiPassthroughActorState) =
                async {
                    let! msg = inbox.Receive()

                    let! state =
                        match msg with
                        | ActorMessage.PullReply(route, rc) ->
                            task {
                                let! resp = getZkbApi state.lastZkbRequest route
                                (resp :> obj) |> rc.Reply

                                return { ZkbApiPassthroughActorState.lastZkbRequest = System.DateTime.UtcNow }
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
