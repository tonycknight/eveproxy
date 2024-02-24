namespace eveproxy.evewho

open System
open eveproxy
open Microsoft.Extensions.Logging

type private EvewhoApiPassthroughActorState =
    { lastEvehoRequest: System.DateTime // TODO: not needed
      thrrottling: Map<DateTime, int> }

    static member empty =
        { EvewhoApiPassthroughActorState.lastEvehoRequest = System.DateTime.MinValue
          thrrottling = Map.empty }

type EvewhoApiPassthroughActor(hc: IExternalHttpClient, logFactory: ILoggerFactory, config: AppConfiguration) =
    let log = logFactory.CreateLogger<EvewhoApiPassthroughActor>()

    let throttle = Throttling.windowThrottling 30 10

    let checkThrottling (counts: Map<DateTime, int>) =
        task {
            let (newCounts, wait) = throttle counts DateTime.UtcNow
            do! System.Threading.Tasks.Task.Delay wait
            return newCounts
        }


    let rec getEvewhoApiIterate throttling count url =
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
                    | HttpTooManyRequestsResponse _ -> getEvewhoApiIterate newThrottling (count - 1) url
            with ex ->
                log.LogError(ex.Message, ex)
                return (throttling, HttpErrorRequestResponse(Net.HttpStatusCode.InternalServerError, ""))
        }

    let getEvewhoApi throttling route =
        let url = $"{config.evewhoApiUrl}{route}"
        getEvewhoApiIterate throttling 10 url

    let actor =
        MailboxProcessor<ActorMessage>.Start(fun inbox ->
            let rec loop (state: EvewhoApiPassthroughActorState) =
                async {
                    let! msg = inbox.Receive()

                    let! state =
                        match msg with
                        | ActorMessage.PullReply(route, rc) ->
                            task {
                                let! (throttling, resp) = getEvewhoApi state.thrrottling route
                                (resp :> obj) |> rc.Reply

                                return { state with thrrottling = throttling }
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
