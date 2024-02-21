namespace eveproxy.zkb

open eveproxy
open Microsoft.Extensions.Logging

type private ZkbApiPassthroughActorState = {
    lastZkbRequest: System.DateTime
}
with 
    static member empty = { ZkbApiPassthroughActorState.lastZkbRequest = System.DateTime.MinValue }

type ZkbApiPassthroughActor
    (hc: IExternalHttpClient, stats: IApiStatsActor, logFactory: ILoggerFactory, config: AppConfiguration) =
    let log = logFactory.CreateLogger<ZkbApiPassthroughActor>()

    let actor =
        MailboxProcessor<ActorMessage>.Start(fun inbox ->
            let rec loop (state: ZkbApiPassthroughActorState) =
                async {
                    let! msg = inbox.Receive()

                    let! state =
                        match msg with
                        | ActorMessage.PullReply(route, rc) ->
                            task {
                                // TODO: need to fetch...
                                (HttpOkRequestResponse(System.Net.HttpStatusCode.OK, "TODO: Fetching from zkb API")
                                :> obj)
                                |> rc.Reply 
                                
                                return { ZkbApiPassthroughActorState.lastZkbRequest = System.DateTime.UtcNow }
                            } |> Async.AwaitTask
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

