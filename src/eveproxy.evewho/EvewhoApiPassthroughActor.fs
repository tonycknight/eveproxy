namespace eveproxy.evewho

open System
open eveproxy
open Microsoft.Extensions.Logging

type private EvewhoApiPassthroughActorState =
    { lastEvehoRequest: System.DateTime }

    static member empty =
        { EvewhoApiPassthroughActorState.lastEvehoRequest = System.DateTime.MinValue }

type EvewhoApiPassthroughActor
    (hc: IExternalHttpClient, logFactory: ILoggerFactory, config: AppConfiguration) =
    let log = logFactory.CreateLogger<EvewhoApiPassthroughActor>()

    let pause (lastPoll: DateTime) =
        task {
            let limit = TimeSpan.FromSeconds(1.)
            let diff = DateTime.UtcNow - lastPoll

            let duration =
                if diff < limit then limit
                else if diff > limit then TimeSpan.Zero
                else diff

            if duration > TimeSpan.Zero then
                log.LogTrace $"Waiting {duration} for Evewho API..."
                do! System.Threading.Tasks.Task.Delay duration
        }

    // TODO: 10 requests per 30 seconds...
    let rec getEvewhoApiIterate lastPoll count url =
        task {
            try
                do! pause lastPoll

                $"GET [{url}] iteration #{count}..." |> log.LogTrace
                let! resp = hc.GetAsync url

                $"GET {HttpRequestResponse.loggable resp} received from [{url}]."
                |> log.LogTrace

                return!
                    match resp with
                    | HttpTooManyRequestsResponse _ when count <= 0 -> resp |> Threading.toTaskResult
                    | HttpOkRequestResponse _
                    | HttpErrorRequestResponse _
                    | HttpExceptionRequestResponse _ -> resp |> Threading.toTaskResult
                    | HttpTooManyRequestsResponse _ -> getEvewhoApiIterate DateTime.UtcNow (count - 1) url
            with ex ->
                log.LogError(ex.Message, ex)
                return HttpErrorRequestResponse(Net.HttpStatusCode.InternalServerError, "")
        }

    let getEvewhoApi lastPoll route =
        let url = $"{config.evewhoApiUrl}{route}"
        getEvewhoApiIterate lastPoll 10 url

    let actor =
        MailboxProcessor<ActorMessage>.Start(fun inbox ->
            let rec loop (state: EvewhoApiPassthroughActorState) =
                async {
                    let! msg = inbox.Receive()

                    let! state =
                        match msg with
                        | ActorMessage.PullReply(route, rc) ->
                            task {
                                let! resp = getEvewhoApi state.lastEvehoRequest route
                                (resp :> obj) |> rc.Reply

                                return { EvewhoApiPassthroughActorState.lastEvehoRequest = System.DateTime.UtcNow }
                            }
                            |> Async.AwaitTask
                        | _ -> async { return state }

                    return! loop state
                }

            EvewhoApiPassthroughActorState.empty |> loop)

    interface IEvewhoApiPassthroughActor with
        member this.GetStats() =
            task {
                return
                    { ActorStats.name = (typedefof<EvewhoApiPassthroughActor>).FullName
                      queueCount = actor.CurrentQueueLength
                      childStats = [] }
            }

        member this.Post(msg: ActorMessage) = actor.Post msg

        member this.Get(route: string) =
            task {
                let! r = actor.PostAndAsyncReply(fun rc -> ActorMessage.PullReply(route, rc))

                return (r :?> HttpRequestResponse)
            }
