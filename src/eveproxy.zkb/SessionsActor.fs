﻿namespace eveproxy.zkb

open System
open System.Threading.Tasks
open eveproxy
open Microsoft.Extensions.Logging

type private SessionsActorState =
    { sessions: Map<string, ISessionActor> }

type SessionsActor
    (
        stats: IApiStatsActor,
        config: AppConfiguration,
        logFactory: ILoggerFactory,
        killReader: IKillmailReader,
        timeProvider: ITimeProvider,
        queueFactory: IKillmailReferenceQueueFactory
    ) =
    let defaultSessionName = ""
    let log = logFactory.CreateLogger<SessionsActor>()
    let maxSessionAge = config.RedisqSessionMaxAge()
    let scheduledMaint = new System.Timers.Timer(TimeSpan.FromMinutes(1))

    let getStateActor (name: string) (state: SessionsActorState) =
        let actor =
            match state.sessions |> Map.tryFind name with
            | Some a -> a
            | None ->
                sprintf "Creating session [%s]" name |> log.LogTrace
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


    let findSessionsToDestroy (state: SessionsActorState) =
        task {
            let! results =
                state.sessions
                |> Seq.filter (fun a -> a.Key <> defaultSessionName)
                |> Seq.map (fun a ->
                    task {
                        let! t = a.Value.GetLastPullTime()
                        return (a.Key, a.Value, t)
                    })
                |> Array.ofSeq
                |> Threading.whenAll

            let exp = timeProvider.GetUtcNow().Add(-maxSessionAge)

            return
                results
                |> Seq.filter (fun (_, _, t) -> t < exp)
                |> Seq.map (fun (k, a, _) -> (k, a))
                |> List.ofSeq
        }


    let postSessionsToDestroy (state: SessionsActorState) (sessions: (string * ISessionActor) list) =
        match sessions with
        | [] ->
            "No sessions found to destroy." |> log.LogTrace
            state
        | sessions ->
            "Starting session destruction..." |> log.LogTrace

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
        { SessionsActorState.sessions = Map.empty } |> getStateActor defaultSessionName

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
                        | _ -> async { return state }

                    return! loop state
                }

            let state, _ = initActorState ()
            state |> loop)

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

        member this.Post(msg: ActorMessage) = actor.Post msg

        member this.GetNext(name: string) =
            task {
                let! r = actor.PostAndAsyncReply(fun rc -> ActorMessage.PullReply(name, rc))

                return
                    match r :?> ActorMessage with
                    | Entity p -> p :?> KillPackageData
                    | _ -> KillPackageData.empty
            }
