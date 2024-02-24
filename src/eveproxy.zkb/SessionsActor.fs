namespace eveproxy.zkb

open System
open eveproxy
open Microsoft.Extensions.Logging

type private SessionsActorState =
    { sessions: Map<string, ISessionActor> }

type SessionsActor
    (
        stats: IZkbStatsActor,
        config: AppConfiguration,
        logFactory: ILoggerFactory,
        killReader: IKillmailReader,
        timeProvider: ITimeProvider,
        queueFactory: IKillmailReferenceQueueFactory,
        queueFinder: IKillmailReferenceQueueFinder
    ) =
    let defaultSessionName = KillmailReferenceQueues.defaultQueueName
    let log = logFactory.CreateLogger<SessionsActor>()
    let maxSessionAge = config.RedisqSessionMaxAge()
    let scheduledMaint = new System.Timers.Timer(TimeSpan.FromMinutes(1))

    let getStateActor (name: string) (state: SessionsActorState) =
        let actor =
            match state.sessions |> Map.tryFind name with
            | Some a -> a
            | None ->
                sprintf "Creating session [%s]" name |> log.LogInformation
                new SessionActor(name, logFactory, stats, killReader, queueFactory)

        let sessions = state.sessions |> Map.add name actor
        let state = { SessionsActorState.sessions = sessions }
        (state, actor)

    let onPush state package =
        async {
            state.sessions
            |> Seq.iter (fun a -> package |> ActorMessage.Entity |> a.Value.Post)

            return state
        }

    let onPullNext state name (rc: AsyncReplyChannel<obj>) =
        task {
            let state, actor = getStateActor name state
            let! package = actor.GetNext()

            (package :> obj) |> ActorMessage.Entity |> rc.Reply

            return state
        }

    let sessionStats (state: SessionsActorState) =
        state.sessions
        |> Map.values
        |> Seq.map (fun a -> a.GetStats())
        |> Array.ofSeq
        |> Threading.whenAll

    let storageStats (state: SessionsActorState) =
        state.sessions
        |> Map.values
        |> Seq.map (fun a -> a.GetStorageStats())
        |> Array.ofSeq
        |> Threading.whenAll

    let findSessionsToDestroy (state: SessionsActorState) =
        task {
            let! results =
                state.sessions
                |> Seq.filter (fun a -> a.Key <> defaultSessionName)
                |> Seq.map (fun a ->
                    task {
                        let! t = a.Value.GetLastPullTime()
                        let c = a.Value.GetCreationTime()
                        return (a.Key, a.Value, t, c)
                    })
                |> Array.ofSeq
                |> Threading.whenAll

            let exp = timeProvider.GetUtcNow().Add(-maxSessionAge)

            return
                results
                |> Seq.filter (fun (_, _, t, c) -> (t > DateTime.MinValue && t < exp) || (c < exp))
                |> Seq.map (fun (k, a, _, _) -> (k, a))
                |> List.ofSeq
        }


    let postSessionsToDestroy (state: SessionsActorState) (sessions: (string * ISessionActor) list) =
        match sessions with
        | [] ->
            "No sessions found to destroy." |> log.LogTrace
            state
        | sessions ->
            sessions
            |> List.length
            |> sprintf "Starting destruction of %i session(s)..."
            |> log.LogInformation

            let cleanSessions =
                sessions
                |> Seq.map fst
                |> Seq.fold (fun s n -> s |> Map.remove n) state.sessions

            sessions
            |> Seq.iter (fun (k, a) ->
                $"Initiating shutdown of session [{k}]" |> log.LogTrace
                ActorMessage.Destroy k |> a.Post)

            { SessionsActorState.sessions = cleanSessions }

    let initActorState () =
        let actors =
            queueFinder.GetNames()
            |> Seq.filter (fun n -> n <> KillmailReferenceQueues.defaultQueueName)
            |> Seq.map getStateActor
            |> List.ofSeq

        let actors = (getStateActor defaultSessionName) :: actors

        actors
        |> List.fold (fun s f -> f s |> fst) { SessionsActorState.sessions = Map.empty }


    let actor =
        MailboxProcessor<ActorMessage>.Start(fun inbox ->
            let rec loop (state: SessionsActorState) =
                async {
                    let! msg = inbox.Receive()

                    let! state =
                        match msg with
                        | Entity e when (e :? KillPackageData) -> onPush state e
                        | PullReply(url, rc) -> onPullNext state url rc |> Async.AwaitTask
                        | ScheduledMaintenance ->
                            async {
                                $"Finding inactive sessions to destroy after {maxSessionAge}..." |> log.LogTrace
                                let! names = findSessionsToDestroy state |> Async.AwaitTask
                                return names |> postSessionsToDestroy state
                            }
                        | ChildStats(rc) ->
                            async {
                                let! stats = sessionStats state |> Async.AwaitTask
                                stats |> rc.Reply
                                return state
                            }
                        | StorageStats(rc) ->
                            async {
                                let! stats = storageStats state |> Async.AwaitTask
                                stats |> Array.collect id |> rc.Reply
                                return state
                            }
                        | _ -> async { return state }

                    return! loop state
                }

            initActorState () |> loop)

    do scheduledMaint.Elapsed.Add(fun _ -> ActorMessage.ScheduledMaintenance |> actor.Post)
    do scheduledMaint.Start()


    interface ISessionsActor with
        member this.GetStats() =
            task {
                let main =
                    { ActorStats.name = (typedefof<SessionsActor>).FullName
                      queueCount = actor.CurrentQueueLength
                      childStats = [] }


                let! stats = actor.PostAndAsyncReply(fun rc -> ActorMessage.ChildStats rc)

                return
                    { main with
                        childStats = (stats |> List.ofArray) }
            }

        member this.GetStorageStats() =
            task { return! actor.PostAndAsyncReply(fun rc -> ActorMessage.StorageStats rc) }

        member this.Post(msg: ActorMessage) = actor.Post msg

        member this.GetNext(name: string) =
            task {
                let name = (if name = "" then defaultSessionName else name) |> Strings.toLower

                let! r = actor.PostAndAsyncReply(fun rc -> ActorMessage.PullReply(name, rc))

                return
                    match r :?> ActorMessage with
                    | Entity p -> p :?> KillPackageData
                    | _ -> KillPackageData.empty
            }
