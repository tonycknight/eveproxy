namespace eveproxy.esi

open System
open System.Net
open eveproxy
open Microsoft.Extensions.Logging

type private EsiApiPassthroughActorState =
    { errorLimitRemaining: int
      errorLimitReset: DateTime }

type EsiApiPassthroughActor(hc: IExternalHttpClient, logFactory: ILoggerFactory, config: AppConfiguration) =

    [<Literal>]
    let errorLimitResetHeader = "x-esi-error-limit-reset"

    [<Literal>]
    let errorLimitRemainHeader = "x-esi-error-limit-remain"

    [<Literal>]
    let errorLimitReached = "x-esi-error-limited"

    let errorLimit = config.EsiMinimumErrorLimit()
    let retryCount = config.EsiRetryCount()

    let log = logFactory.CreateLogger<EsiApiPassthroughActor>()

    let intHeaderValue defaultValue name =
        HttpRequestResponse.headerValues name
        >> Seq.tryHead
        >> Option.map (Strings.toInt defaultValue)
        >> Option.defaultValue defaultValue

    let errorsRemaining = intHeaderValue 0 errorLimitRemainHeader

    let errorsResetWait =
        intHeaderValue 60 errorLimitResetHeader >> TimeSpan.FromSeconds

    let rec getEsiApiIterate (state: EsiApiPassthroughActorState) count url =
        task {
            try
                let now = DateTime.UtcNow

                $"GET [{url}] iteration #{count}..." |> log.LogTrace
                let! resp = hc.GetAsync url

                $"GET {HttpRequestResponse.loggable resp} received from [{url}]."
                |> log.LogTrace

                let state =
                    { state with
                        errorLimitRemaining = errorsRemaining resp
                        errorLimitReset = now.Add(errorsResetWait resp) }

                return!
                    match resp with
                    | HttpBadGatewayResponse(_) ->
                        getEsiApiIterate state (count - 1) url
                    | _ when state.errorLimitRemaining <= errorLimit ->
                        $"{state.errorLimitRemaining} received... breaking circuit" |> log.LogWarning
                        (state, HttpTooManyRequestsResponse([])) |> Threading.toTaskResult
                    | _ -> (state, resp) |> Threading.toTaskResult
            with ex ->
                log.LogError(ex.Message, ex)
                return (state, HttpErrorRequestResponse(Net.HttpStatusCode.InternalServerError, "", []))
        }

    let getEsiApi (state: EsiApiPassthroughActorState) route =
        if
            state.errorLimitRemaining <= errorLimit
            && DateTime.UtcNow < state.errorLimitReset
        then
            $"{state.errorLimitRemaining} received... breaking circuit" |> log.LogWarning
            (state, HttpTooManyRequestsResponse([])) |> Threading.toTaskResult
        else
            $"{config.esiApiUrl}{route}" |> getEsiApiIterate state retryCount

    let actor =
        MailboxProcessor<ActorMessage>.Start(fun inbox ->
            let rec loop (state: EsiApiPassthroughActorState) =
                async {
                    let! msg = inbox.Receive()

                    let! state =
                        match msg with
                        | ActorMessage.PullReply(route, rc) ->
                            task {
                                let! (state, resp) = getEsiApi state route
                                (resp :> obj) |> rc.Reply

                                return state
                            }
                            |> Async.AwaitTask
                        | _ -> async { return state }

                    return! loop state
                }

            { EsiApiPassthroughActorState.errorLimitRemaining = errorLimit
              errorLimitReset = DateTime.MinValue }
            |> loop)

    interface IEsiApiPassthroughActor with
        member this.GetStats() =
            task {
                return
                    { ActorStats.name = (typedefof<EsiApiPassthroughActor>).FullName
                      queueCount = actor.CurrentQueueLength
                      childStats = [] }
            }

        member this.Post(msg: ActorMessage) = actor.Post msg

        member this.Get(route: string) =
            task {
                let! r = actor.PostAndAsyncReply(fun rc -> ActorMessage.PullReply(route, rc))

                return (r :?> HttpRequestResponse)
            }
