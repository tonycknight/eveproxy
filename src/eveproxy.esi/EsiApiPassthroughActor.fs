namespace eveproxy.esi

open System
open eveproxy
open Microsoft.Extensions.Logging

type private EsiApiPassthroughActorState =
    { 
        errorLimitRemaining: int
        errorLimitResetSeconds: int
    }

    static member empty = { EsiApiPassthroughActorState.errorLimitRemaining = 0; errorLimitResetSeconds = 0 }

type EsiApiPassthroughActor(hc: IExternalHttpClient, logFactory: ILoggerFactory, config: AppConfiguration) =
    
    [<Literal>]
    let errorLimitResetHeader = "x-esi-error-limit-reset"
    [<Literal>]
    let errorLimitRemainHeader = "x-esi-error-limit-remain"
    [<Literal>]
    let errorLimitReached = "x-esi-error-limited"
    
    let log = logFactory.CreateLogger<EsiApiPassthroughActor>()
        
    let rec getEsiApiIterate count url =
        task {
            try                
                $"GET [{url}] iteration #{count}..." |> log.LogTrace
                let! resp = hc.GetAsync url

                $"GET {HttpRequestResponse.loggable resp} received from [{url}]."
                |> log.LogTrace

                // TODO: get response headers
                return!
                    match resp with
                    | HttpTooManyRequestsResponse _ when count <= 0 -> resp |> Threading.toTaskResult
                    | HttpOkRequestResponse _
                    | HttpErrorRequestResponse _
                    | HttpExceptionRequestResponse _ -> resp |> Threading.toTaskResult
                    | HttpTooManyRequestsResponse _ -> getEsiApiIterate (count - 1) url
            with ex ->
                log.LogError(ex.Message, ex)
                return HttpErrorRequestResponse(Net.HttpStatusCode.InternalServerError, "")
        }

    let getEsiApi route =
        let url = $"{config.esiApiUrl}{route}"
        getEsiApiIterate 10 url

    let actor =
        MailboxProcessor<ActorMessage>.Start(fun inbox ->
            let rec loop (state: EsiApiPassthroughActorState) =
                async {
                    let! msg = inbox.Receive()

                    let! state =
                        match msg with
                        | ActorMessage.PullReply(route, rc) ->
                            task {
                                let! resp = getEsiApi route
                                (resp :> obj) |> rc.Reply

                                return state // TODO: 
                            }
                            |> Async.AwaitTask
                        | _ -> async { return state }

                    return! loop state
                }

            EsiApiPassthroughActorState.empty |> loop)

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
