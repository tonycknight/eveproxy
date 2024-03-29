﻿namespace eveproxy.zkb

open System
open eveproxy


type ZkbStatsActor() =

    let bumpReceived state (count: ReceivedKills) =
        let ingestion =
            { state.ingestion with
                receivedKills = state.ingestion.receivedKills + count.count }

        { state with ingestion = ingestion }

    let bumpWritten state (count: WrittenKills) =
        let ingestion =
            { state.ingestion with
                writtenKills = state.ingestion.writtenKills + count.count }

        { state with ingestion = ingestion }

    let bumpDistrubuted state (count: DistributedKills) =
        let sessionCount =
            match state.distribution.sessionDistributedKills |> Map.tryFind count.session with
            | Some c -> c + count.count
            | None -> count.count

        { state with
            distribution =
                { DistributionStats.totalDistributedKills = state.distribution.totalDistributedKills + count.count
                  sessionDistributedKills =
                    state.distribution.sessionDistributedKills |> Map.add count.session sessionCount } }

    let actor =
        MailboxProcessor<ActorMessage>.Start(fun inbox ->
            let rec loop (state: ZkbStats) =
                async {
                    let! msg = inbox.Receive()

                    let state =
                        match msg with
                        | ActorMessage.Entity e when (e :? ReceivedKills) ->
                            (e :?> ReceivedKills) |> bumpReceived state
                        | ActorMessage.Entity e when (e :? WrittenKills) -> (e :?> WrittenKills) |> bumpWritten state
                        | ActorMessage.Entity e when (e :? DistributedKills) ->
                            (e :?> DistributedKills) |> bumpDistrubuted state
                        | ActorMessage.PullReply(e, rc) ->
                            (state :> obj) |> rc.Reply
                            state
                        | _ -> state

                    return! loop state
                }

            let state =
                { ZkbStats.ingestion =
                    { IngestionStats.receivedKills = 0
                      writtenKills = 0 }
                  distribution =
                    { DistributionStats.totalDistributedKills = 0
                      sessionDistributedKills = Map.empty } }

            state |> loop)

    interface IZkbStatsActor with
        member this.GetStats() =
            task {
                return
                    { ActorStats.name = (typedefof<ZkbStatsActor>).FullName
                      queueCount = actor.CurrentQueueLength
                      childStats = [] }
            }

        member this.Post(msg: ActorMessage) = actor.Post msg

        member this.GetApiStats() =
            task {
                let! r = actor.PostAndAsyncReply(fun rc -> ActorMessage.PullReply("", rc))

                return r :?> ZkbStats
            }
