namespace eveproxy.zkb

open System
open eveproxy
open Microsoft.Extensions.Logging

type KillWriteActor
    (stats: IApiStatsActor, sessions: ISessionsActor, logFactory: ILoggerFactory, writer: IKillmailWriter) =
    let log = logFactory.CreateLogger<KillWriteActor>()

    let writeKill (kill: KillPackageData) =
        match kill |> KillPackageData.killmailId with
        | Some id ->
            id |> sprintf "--> Received kill [%s]. Sending to write..." |> log.LogTrace
            kill |> writer.WriteAsync |> Async.AwaitTask
        | _ -> async { return kill }

    let countKill (kill: KillPackageData) =
        { WrittenKills.count = 1 } :> obj |> ActorMessage.Entity |> stats.Post
        kill

    let broadcastKill (kill: KillPackageData) =
        kill :> obj |> ActorMessage.Entity |> sessions.Post
        kill

    let actor =
        MailboxProcessor<ActorMessage>.Start(fun inbox ->
            let rec loop () =
                async {
                    let! msg = inbox.Receive()

                    try
                        match msg with
                        | Entity e when (e :? KillPackageData) ->
                            let! kp = (e :?> KillPackageData) |> writeKill
                            kp |> countKill |> broadcastKill |> ignore
                        | _ -> ignore 0
                    with
                    | ex -> log.LogError(ex, ex.Message)
                    return! loop ()
                }

            loop ())

    interface IKillWriteActor with
        member this.GetStats() =
            task {
                return
                    { ActorStats.name = (typedefof<KillWriteActor>).FullName
                      queueCount = actor.CurrentQueueLength
                      childStats = [] }
            }

        member this.Post(msg: ActorMessage) = actor.Post msg
