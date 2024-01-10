namespace eveproxy.zkb

open System
open eveproxy
open Microsoft.Extensions.Logging

type private SessionActorState = { kills: IKillmailReferenceQueue }

type SessionActor
    (
        name: string,
        logFactory: ILoggerFactory,
        stats: IApiStatsActor,
        killReader: IKillmailReader,
        queueFactory: IKillmailReferenceQueueFactory
    ) =
    let log = logFactory.CreateLogger<SessionActor>()

    let onPush (state: SessionActorState) (kill: obj) =
        async {
            let kp = (kill :?> KillPackage)
            let id = kp |> KillPackage.killmailId

            if id |> Option.isSome then
                let id = id |> Option.get
                sprintf "Pushing kill reference [%s] to queue [%s]..." id name |> log.LogTrace
                let kpr = { KillPackageReference.id = id }
                do! kpr |> state.kills.PushAsync |> Async.AwaitTask
                sprintf "Pushed kill reference [%s] to queue [%s]." id name |> log.LogTrace

            return state
        }

    let onPullNext state (rc: AsyncReplyChannel<obj>) =
        async {
            sprintf "Fetching next kill reference for queue [%s]..." name |> log.LogTrace
            let! killRef = state.kills.PullAsync() |> Async.AwaitTask

            let! package =
                match killRef with
                | None -> async { return None }
                | Some kr ->
                    async {
                        sprintf "Fetching kill [%s] by reference for queue [%s]..." kr.id name
                        |> log.LogTrace

                        let! km = kr.id |> killReader.ReadAsync |> Async.AwaitTask

                        if km |> Option.isSome then
                            sprintf "Fetched kill [%s] by reference for queue [%s]." kr.id name
                            |> log.LogTrace

                        return km
                    }


            let package = package |> Option.defaultValue KillPackage.empty
            package :> obj |> ActorMessage.Entity |> rc.Reply

            return state
        }

    let actor =
        MailboxProcessor<ActorMessage>.Start(fun inbox ->
            let rec loop (state: SessionActorState) =
                async {
                    let! msg = inbox.Receive()

                    let! state =
                        match msg with
                        | Entity e when (e :? KillPackage) -> onPush state e
                        | PullReply(url, rc) -> onPullNext state rc
                        | _ -> async { return state }

                    return! loop state
                }

            { SessionActorState.kills = queueFactory.Create name } |> loop)

    interface ISessionActor with
        member this.GetStats() =
            task {
                return
                    { ActorStats.name = (ActorStats.statsName this name)
                      queueCount = actor.CurrentQueueLength
                      childStats = [] }
            }

        member this.Post(msg: ActorMessage) = actor.Post msg

        member this.GetNext() =
            task {
                let! r = actor.PostAndAsyncReply(fun rc -> ActorMessage.PullReply("", rc))

                return
                    match r :?> ActorMessage with
                    | Entity p -> p :?> KillPackage
                    | _ -> KillPackage.empty
            }
