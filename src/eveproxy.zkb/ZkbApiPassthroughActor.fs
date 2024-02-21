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
        let limit = TimeSpan.FromSeconds(1.)
        let diff = DateTime.UtcNow - lastPoll
                
        if diff < limit then limit
        else if diff > limit then TimeSpan.Zero
        else diff
        

    let rec getZkbApiIterate lastPoll countiteration url =
        task {
            try
                do! lastPoll |> pause |> System.Threading.Tasks.Task.Delay 
                
                let! resp = hc.GetAsync url

                return!
                    match resp with
                    | HttpTooManyRequestsResponse _ when countiteration <= 0 -> resp |> eveproxy.Threading.toTaskResult
                    | HttpOkRequestResponse _
                    | HttpErrorRequestResponse _
                    | HttpExceptionRequestResponse _ -> resp |> eveproxy.Threading.toTaskResult
                    | HttpTooManyRequestsResponse _ -> getZkbApiIterate lastPoll (countiteration - 1) url
            with ex ->
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
                // TODO: needs integrating with the rest of stats...
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
