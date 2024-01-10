namespace eveproxy.zkb

open System
open eveproxy


type ApiStatsActor() =

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

    let bumpRouteFetch (state: ApiStats) url count =
        let route =
            match state.routes |> Map.tryFind url with
            | Some rs -> { rs with count = rs.count + count }
            | None ->
                { RouteStatistics.route = url
                  count = count }

        { state with
            routes = state.routes |> Map.add url route }

    let actor =
        MailboxProcessor<ActorMessage>.Start(fun inbox ->
            let rec loop (state: ApiStats) =
                async {
                    let! msg = inbox.Receive()

                    let state =
                        match msg with
                        | ActorMessage.Entity e when (e :? ReceivedKills) ->
                            (e :?> ReceivedKills) |> bumpReceived state
                        | ActorMessage.Entity e when (e :? WrittenKills) -> (e :?> WrittenKills) |> bumpWritten state
                        | ActorMessage.Entity e when (e :? DistributedKills) ->
                            (e :?> DistributedKills) |> bumpDistrubuted state
                        | ActorMessage.RouteFetch(url, count) -> bumpRouteFetch state url count
                        | ActorMessage.PullReply(e, rc) ->
                            (state :> obj) |> rc.Reply
                            state
                        | _ -> state

                    return! loop state
                }

            let state =
                { ApiStats.ingestion =
                    { IngestionStats.receivedKills = 0
                      writtenKills = 0 }
                  distribution =
                    { DistributionStats.totalDistributedKills = 0
                      sessionDistributedKills = Map.empty }
                  routes = Map.empty }

            state |> loop)

    interface IApiStatsActor with
        member this.GetStats() =
            task {
                return
                    { ActorStats.name = (typedefof<ApiStatsActor>).FullName
                      queueCount = actor.CurrentQueueLength
                      childStats = [] }
            }

        member this.Post(msg: ActorMessage) = actor.Post msg

        member this.GetApiStats() =
            task {
                let! r = actor.PostAndAsyncReply(fun rc -> ActorMessage.PullReply("", rc))

                return r :?> ApiStats
            }
