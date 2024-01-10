namespace eveproxy.zkb

open System.Threading.Tasks
open eveproxy
open Microsoft.Extensions.Logging

type private SessionsActorState =
    { sessions: Map<string, ISessionActor> }

type SessionsActor
    (
        stats: IApiStatsActor,
        logFactory: ILoggerFactory,
        killReader: IKillmailReader,
        queueFactory: IKillmailReferenceQueueFactory
    ) =
    let log = logFactory.CreateLogger<SessionsActor>()

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
        task {
            let actors =
                state.sessions |> Map.values |> Seq.map (fun a -> a.GetStats()) |> Array.ofSeq

            let! stats = Task.WhenAll(actors)

            return stats
        }

    let initActorState () =
        { SessionsActorState.sessions = Map.empty } |> getStateActor ""

    let actor =
        MailboxProcessor<ActorMessage>.Start(fun inbox ->
            let rec loop (state: SessionsActorState) =
                async {
                    let! msg = inbox.Receive()

                    let! state =
                        match msg with
                        | Entity e when (e :? KillPackage) -> onPush state e
                        | PullReply(url, rc) -> onPullNext state url rc |> Async.AwaitTask
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
                    | Entity p -> p :?> KillPackage
                    | _ -> KillPackage.empty
            }
