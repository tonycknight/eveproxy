﻿namespace eveproxy.evewho

open System
open eveproxy
open Microsoft.Extensions.Logging

type private EvewhoApiPassthroughActorState =
    { throttling: Map<DateTime, int> }

    static member empty = { EvewhoApiPassthroughActorState.throttling = Map.empty }

type EvewhoApiPassthroughActor
    (hc: IExternalHttpClient, logFactory: ILoggerFactory, metrics: IMetricsTelemetry, config: AppConfiguration) =
    let log = logFactory.CreateLogger<EvewhoApiPassthroughActor>()
    let throttle = config.EveWhoThrottling() |> Throttling.windowThrottling

    let checkThrottling (counts: Map<DateTime, int>) =
        task {
            let (newCounts, wait) = throttle counts DateTime.UtcNow

            if wait > TimeSpan.Zero then
                $"Waiting {wait} before next request to Evewho" |> log.LogTrace

            do! System.Threading.Tasks.Task.Delay wait
            return newCounts
        }

    let instrumentResponse url resp =
        $"GET {HttpRequestResponse.loggable resp} received from [{url}]."
        |> log.LogTrace

        match resp with
        | HttpOkRequestResponse _ -> metrics.SuccessfulEvewhoRequest 1
        | HttpTooManyRequestsResponse _ -> metrics.ThrottledEvewhoRequest 1
        | _ -> metrics.FailedEvewhoRequest 1

    let rec getEvewhoApiIterate throttling count url =
        task {
            try
                let! newThrottling = checkThrottling throttling

                $"GET [{url}] iteration #{count}..." |> log.LogTrace
                let! resp = hc.GetAsync url

                instrumentResponse url resp

                return!
                    match resp with
                    | HttpTooManyRequestsResponse _ when count <= 0 -> (newThrottling, resp) |> Threading.toTaskResult
                    | HttpOkRequestResponse _
                    | HttpErrorRequestResponse _
                    | HttpBadGatewayResponse _
                    | HttpExceptionRequestResponse _ -> (newThrottling, resp) |> Threading.toTaskResult
                    | HttpTooManyRequestsResponse _ -> getEvewhoApiIterate newThrottling (count - 1) url
            with ex ->
                log.LogError(ex.Message, ex)
                return (throttling, HttpErrorRequestResponse(Net.HttpStatusCode.InternalServerError, "", []))
        }

    let getEvewhoApi throttling route =
        let url = $"{config.evewhoApiUrl}{route}"
        getEvewhoApiIterate throttling 10 url

    let actor =
        MailboxProcessor<ActorMessage>.Start(fun inbox ->
            let rec loop (state: EvewhoApiPassthroughActorState) =
                async {
                    let! msg = inbox.Receive()

                    let! state =
                        match msg with
                        | ActorMessage.PullReply(route, rc) ->
                            task {
                                let! (throttling, resp) = getEvewhoApi state.throttling route
                                (resp :> obj) |> rc.Reply

                                return { EvewhoApiPassthroughActorState.throttling = throttling }
                            }
                            |> Async.AwaitTask
                        | _ -> async { return state }

                    return! loop state
                }

            EvewhoApiPassthroughActorState.empty |> loop)

    interface IEvewhoApiPassthroughActor with
        member this.GetStats() =
            task {
                return
                    { ActorStats.name = (typedefof<EvewhoApiPassthroughActor>).FullName
                      queueCount = actor.CurrentQueueLength
                      childStats = [] }
            }

        member this.Post(msg: ActorMessage) = actor.Post msg

        member this.Get(route: string) =
            task {
                let! r = actor.PostAndAsyncReply(fun rc -> ActorMessage.PullReply(route, rc))

                return (r :?> HttpRequestResponse)
            }
