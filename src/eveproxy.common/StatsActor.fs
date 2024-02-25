namespace eveproxy


type StatsActor() =

    let bumpRouteFetch (state: ApiRouteStatistics) (method, url, count) =
        let key = $"{method} {url}"

        let route =
            match state.routes |> Map.tryFind key with
            | Some rs -> { rs with count = rs.count + count }
            | None ->
                { RouteStatistics.route = key
                  count = count }

        { ApiRouteStatistics.routes = state.routes |> Map.add key route }

    let actor =
        MailboxProcessor<ActorMessage>.Start(fun inbox ->
            let rec loop (state: ApiRouteStatistics) =
                async {
                    let! msg = inbox.Receive()

                    let state =
                        match msg with
                        | ActorMessage.RouteFetch(method, url, count) ->
                            bumpRouteFetch state (method, Strings.toLower url, count)
                        | ActorMessage.PullReply(e, rc) ->
                            (state :> obj) |> rc.Reply
                            state
                        | _ -> state

                    return! loop state
                }

            let state = { routes = Map.empty }

            state |> loop)

    interface IStatsActor with
        member this.GetStats() =
            task {
                return
                    { ActorStats.name = (typedefof<StatsActor>).FullName
                      queueCount = actor.CurrentQueueLength
                      childStats = [] }
            }

        member this.Post(msg: ActorMessage) = actor.Post msg

        member this.GetApiStats() =
            task {
                let! r = actor.PostAndAsyncReply(fun rc -> ActorMessage.PullReply("", rc))

                return r :?> ApiRouteStatistics
            }
