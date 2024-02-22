namespace eveproxy.zkb

open System
open eveproxy
open Microsoft.Extensions.Logging

type private SessionActorState =
    { kills: IKillmailReferenceQueue
      lastPull: DateTime
      lastPush: DateTime
      pullCount: uint64
      pushCount: uint64 }

    static member bumpPull(state: SessionActorState) =
        { state with
            lastPull = DateTime.UtcNow
            pullCount = state.pullCount + 1UL }

    static member bumpPush(state: SessionActorState) =
        { state with
            lastPush = DateTime.UtcNow
            pushCount = state.pushCount + 1UL }

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

                let kpr =
                    { KillPackageReferenceData.killmailId = id
                      _id = MongoBson.id ()
                      created = DateTime.UtcNow }

                try
                    do! kpr |> state.kills.PushAsync |> Async.AwaitTask
                with ex ->
                    log.LogError(ex, ex.Message)

            return state |> SessionActorState.bumpPush
        }

    let getKill (kr: KillPackageReferenceData) =
        async {
            sprintf "Fetching kill [%s] by reference from queue [%s]..." kr.killmailId name
            |> log.LogTrace

            let! km = kr.killmailId |> killReader.ReadAsync |> Async.AwaitTask

            if km |> Option.isSome then
                sprintf "Fetched kill [%s] by reference from queue [%s]." kr.killmailId name
                |> log.LogTrace

            return km
        }

    let onPullNext state (rc: AsyncReplyChannel<obj>) =
        async {

            try
                let! (state, package) =
                    if state.pullCount >= state.pushCount then
                        async { return (state, None) }
                    else
                        async {
                            sprintf "Fetching next kill reference from queue [%s]..." name |> log.LogTrace
                            let! killRef = state.kills.PullAsync() |> Async.AwaitTask

                            return!
                                match killRef with
                                | None -> async { return (state, None) }
                                | Some kr ->
                                    async {
                                        let! km = getKill kr
                                        return (SessionActorState.bumpPull state, km)
                                    }
                        }

                let package = package |> Option.defaultValue KillPackageData.empty
                package :> obj |> ActorMessage.Entity |> rc.Reply
                return state
            with ex ->
                log.LogError(ex, ex.Message)
                KillPackageData.empty :> obj |> ActorMessage.Entity |> rc.Reply
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

            try
                do! state.kills.ClearAsync() |> Async.AwaitTask
            with ex ->
                log.LogError(ex, ex.Message)

            $"Shut down session [{name}]." |> log.LogInformation
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

            let queue = queueFactory.Create name

            let count = uint64 (queue.GetCountAsync().Result)

            { SessionActorState.kills = queue
              lastPull = DateTime.MinValue
              lastPush = DateTime.MinValue
              pullCount = 0UL
              pushCount = count }
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

        member this.GetCreationTime() = creation

        member this.GetLastPullTime() =
            task { return! actor.PostAndAsyncReply(fun rc -> ActorMessage.LastUpdate rc) }
