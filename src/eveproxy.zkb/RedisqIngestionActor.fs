namespace eveproxy.zkb

open System
open System.Threading.Tasks
open eveproxy
open Microsoft.Extensions.Logging

type private RedisqIngestionActorState = { receivedKills: uint64 }

type RedisqIngestionActor
    (
        hc: IExternalHttpClient,
        stats: IZkbStatsActor,
        logFactory: ILoggerFactory,
        writer: IKillmailWriter,
        sessions: ISessionsActor
    ) =
    let log = logFactory.CreateLogger<RedisqIngestionActor>()

    let logKmIdAction format logger (kill: KillPackageData) =
        kill
        |> KillPackageData.killmailId
        |> Option.defaultValue ""
        |> sprintf format
        |> logger

        kill

    let logKmReceipt = logKmIdAction "--> Received kill [%s]." log.LogInformation

    let logKmNoop =
        logKmIdAction "--> No write of kill [%s] - no further action." log.LogTrace

    let logKmCompletion =
        logKmIdAction "--> Finished processing kill [%s]." log.LogTrace

    let logKmDeduplication =
        logKmIdAction "--> Ignoring kill [%s] as duplicate." log.LogTrace

    let logKmBroadcast = logKmIdAction "--> Broadcasting kill [%s]." log.LogTrace

    let parse body =
        try
            body |> Newtonsoft.Json.JsonConvert.DeserializeObject<KillPackageData> |> Some
        with ex ->
            body
            |> Strings.leftSnippet 20
            |> sprintf "Unrecogniseed JSON returned from RedisQ: %s"
            |> log.LogWarning

            None

    let getNext url =
        task {
            log.LogTrace "Getting next kill package..."
            let! rep = hc.GetAsync url

            return
                match rep with
                | HttpOkRequestResponse(_, body, _, _) ->
                    match parse body with
                    | Some package -> Choice1Of3 package
                    | _ -> Choice3Of3 None
                | HttpTooManyRequestsResponse _ -> 1 |> TimeSpan.FromMinutes |> Choice2Of3
                | _ -> Choice3Of3 None
        }

    let countKillReceipt (kill: KillPackageData) =
        { ReceivedKills.count = 1 } :> obj |> ActorMessage.Entity |> stats.Post
        kill

    let constructKill (kill: KillPackageData) = { kill with created = DateTime.UtcNow }

    let countKillWrite (kill: KillPackageData) =
        { WrittenKills.count = 1 } :> obj |> ActorMessage.Entity |> stats.Post
        kill

    let broadcastKill (kill: KillPackageData) =
        kill :> obj |> ActorMessage.Entity |> sessions.Post
        kill

    let handleKill (state: RedisqIngestionActorState) (kill: KillPackageData) =
        task {
            if (kill |> KillPackageData.killmailId |> Option.isNone) then
                log.LogWarning "Killmail received without a killmailID."
            else
                let! writeResult = kill |> logKmReceipt |> constructKill |> countKillReceipt |> writer.WriteAsync

                let kill =
                    match writeResult with
                    | KillmailWriteResult.Noop -> kill |> logKmNoop
                    | KillmailWriteResult.Inserted kill -> kill |> countKillWrite |> logKmBroadcast |> broadcastKill
                    | KillmailWriteResult.Updated kill -> kill |> countKillWrite |> logKmDeduplication

                kill |> logKmCompletion |> ignore

            return { RedisqIngestionActorState.receivedKills = state.receivedKills + 1UL }
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
                                    | Choice1Of3 kp when kp = KillPackageData.empty ->
                                        "Empty package received." |> log.LogTrace
                                        async { return state }
                                    | Choice1Of3 kp -> kp |> handleKill state |> Async.AwaitTask
                                    | Choice2Of3 ts -> wait state ts |> Async.AwaitTask
                                    | _ -> wait state TimeSpan.Zero |> Async.AwaitTask

                                ActorMessage.Pull url |> inbox.Post

                                return state
                            }
                        | _ -> async { return state }

                    return! loop state
                }

            { RedisqIngestionActorState.receivedKills = 0UL } |> loop)

    interface IRedisqIngestionActor with
        member this.GetStats() =
            task {
                return
                    { ActorStats.name = (typedefof<RedisqIngestionActor>).FullName
                      queueCount = actor.CurrentQueueLength
                      childStats = [] }
            }

        member this.Post(msg: ActorMessage) = actor.Post msg
