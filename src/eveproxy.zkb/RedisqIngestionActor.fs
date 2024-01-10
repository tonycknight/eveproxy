namespace eveproxy.zkb

open System
open System.Threading.Tasks
open eveproxy
open Microsoft.Extensions.Logging

type private RedisqIngestionActorState = { receivedKills: int64 }

type RedisqIngestionActor
    (hc: IExternalHttpClient, write: IKillWriteActor, stats: IApiStatsActor, logFactory: ILoggerFactory) =
    let log = logFactory.CreateLogger<RedisqIngestionActor>()

    let parse body =
        try
            body |> Newtonsoft.Json.JsonConvert.DeserializeObject<KillPackage> |> Some
        with ex ->
            body
            |> Strings.leftSnippet 20
            |> sprintf "Unrecogniseed JSON returned from RedisQ: %s"
            |> log.LogWarning

            None

    let getNext url =
        task {
            log.LogTrace("Getting next kill package...")
            let! rep = hc.GetAsync url

            return
                match rep with
                | HttpOkRequestResponse(_, body) ->
                    match parse body with
                    | Some package -> Choice1Of3 package
                    | _ -> Choice3Of3 None
                | HttpTooManyRequestsResponse _ -> 1 |> TimeSpan.FromMinutes |> Choice2Of3
                | _ -> Choice3Of3 None
        }

    // TOOD: output to a stream?
    let forwardKill (state: RedisqIngestionActorState) (kill: KillPackage) =
        task {
            if kill <> KillPackage.empty then
                kill :> obj |> ActorMessage.Entity |> write.Post
                { ReceivedKills.count = 1 } :> obj |> ActorMessage.Entity |> stats.Post

            return state
        }

    let wait state (ts: TimeSpan) =
        task {
            do! Task.Delay ts
            return state
        }

    let actor =
        MailboxProcessor<ActorMessage>.Start(fun inbox ->
            let rec loop (state: RedisqIngestionActorState) =
                async {
                    let! msg = inbox.Receive()

                    let! state =
                        match msg with
                        | ActorMessage.Pull url ->
                            async {
                                let! kill = url |> getNext |> Async.AwaitTask

                                let! state =
                                    match kill with
                                    | Choice1Of3 kp when kp <> KillPackage.empty ->
                                        kp
                                        |> KillPackage.killmailId
                                        |> Option.defaultValue ""
                                        |> sprintf "--> Received kill [%s]."
                                        |> log.LogTrace

                                        forwardKill state kp |> Async.AwaitTask

                                    | Choice1Of3 kp ->
                                        "Empty package received." |> log.LogTrace
                                        async { return state }
                                    | Choice2Of3 ts -> wait state ts |> Async.AwaitTask
                                    | _ -> wait state TimeSpan.Zero |> Async.AwaitTask

                                ActorMessage.Pull url |> inbox.Post

                                return state
                            }
                        | _ -> async { return state }

                    return! loop state
                }

            { RedisqIngestionActorState.receivedKills = 0L } |> loop)

    interface IRedisqIngestionActor with
        member this.GetStats() =
            task {
                return
                    { ActorStats.name = (typedefof<RedisqIngestionActor>).FullName
                      queueCount = actor.CurrentQueueLength
                      childStats = [] }
            }

        member this.Post(msg: ActorMessage) = actor.Post msg
