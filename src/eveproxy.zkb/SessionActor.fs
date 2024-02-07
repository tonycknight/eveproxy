namespace eveproxy.zkb

open System
open eveproxy
open Microsoft.Extensions.Logging

type private SessionActorState =
    { kills: IKillmailReferenceQueue
      lastPull: DateTime }

type SessionActor
    (
        name: string,
        logFactory: ILoggerFactory,
        stats: IApiStatsActor,
        killReader: IKillmailReader,
        queueFactory: IKillmailReferenceQueueFactory
    ) =
    let creation = DateTime.UtcNow
    let log = logFactory.CreateLogger<SessionActor>()

    let onPush (state: SessionActorState) (kill: obj) =
        async {
            let kp = (kill :?> KillPackageData)
            let id = kp |> KillPackageData.killmailId

            if id |> Option.isSome then
                let id = id |> Option.get
                sprintf "Pushing kill reference [%s] to queue [%s]..." id name |> log.LogTrace

                let kpr =
                    { KillPackageReferenceData.killmailId = id
                      _id = MongoBson.id () }

                do! kpr |> state.kills.PushAsync |> Async.AwaitTask
                sprintf "Pushed kill reference [%s] to queue [%s]." id name |> log.LogTrace

            return state
        }

    let onPullNext state (rc: AsyncReplyChannel<obj>) =
        async {
            let state =
                { state with
                    lastPull = DateTime.UtcNow }

            sprintf "Fetching next kill reference for queue [%s]..." name |> log.LogTrace
            let! killRef = state.kills.PullAsync() |> Async.AwaitTask

            let! package =
                match killRef with
                | None -> async { return None }
                | Some kr ->
                    async {
                        sprintf "Fetching kill [%s] by reference for queue [%s]..." kr.killmailId name
                        |> log.LogTrace

                        let! km = kr.killmailId |> killReader.ReadAsync |> Async.AwaitTask

                        if km |> Option.isSome then
                            sprintf "Fetched kill [%s] by reference for queue [%s]." kr.killmailId name
                            |> log.LogTrace

                        return km
                    }


            let package = package |> Option.defaultValue KillPackageData.empty
            package :> obj |> ActorMessage.Entity |> rc.Reply

            return state
        }

    let storageStats (state: SessionActorState) =
        task {
            let! count = state.kills.GetCountAsync()

            return
                { StorageStats.name = name
                  count = count }
        }

    let shutdown state =
        async {
            $"Shutting down session [{name}]..." |> log.LogTrace

            do! state.kills.ClearAsync() |> Async.AwaitTask

            $"Shut down session [{name}]." |> log.LogTrace
            return state
        }

    let actor =
        MailboxProcessor<ActorMessage>.Start(fun inbox ->
            let rec loop (state: SessionActorState) =
                async {
                    let! msg = inbox.Receive()

                    let! state =
                        match msg with
                        | Entity e when (e :? KillPackageData) -> onPush state e
                        | PullReply(url, rc) -> onPullNext state rc
                        | Destroy n -> shutdown state
                        | LastUpdate rc ->
                            async {
                                rc.Reply state.lastPull
                                return state
                            }
                        | StorageStats rc ->
                            async {
                                let! result = storageStats state |> Async.AwaitTask
                                rc.Reply [| result |]
                                return state
                            }
                        | _ -> async { return state }

                    return! loop state
                }

            { SessionActorState.kills = queueFactory.Create name
              lastPull = DateTime.MinValue }
            |> loop)

    interface ISessionActor with
        member this.GetStats() =
            task {
                return
                    { ActorStats.name = (ActorStats.statsName this name)
                      queueCount = actor.CurrentQueueLength
                      childStats = [] }
            }

        member this.GetStorageStats() =
            task { return! actor.PostAndAsyncReply(fun rc -> ActorMessage.StorageStats rc) }


        member this.Post(msg: ActorMessage) = actor.Post msg

        member this.GetNext() =
            task {
                let! r = actor.PostAndAsyncReply(fun rc -> ActorMessage.PullReply("", rc))

                return
                    match r :?> ActorMessage with
                    | Entity p -> p :?> KillPackageData
                    | _ -> KillPackageData.empty
            }

        member this.GetCreationTime () = creation

        member this.GetLastPullTime() =
            task { return! actor.PostAndAsyncReply(fun rc -> ActorMessage.LastUpdate rc) }
