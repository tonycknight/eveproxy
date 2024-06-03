namespace eveproxy.esi

open System
open eveproxy
open Microsoft.Extensions.Logging

type EsiErrorThrottling = 
    { errorLimitRemaining: int
      errorLimitReset: DateTime }

module Esi=
    [<Literal>]
    let errorLimitResetHeader = "x-esi-error-limit-reset"

    [<Literal>]
    let errorLimitRemainHeader = "x-esi-error-limit-remain"

    [<Literal>]
    let errorLimitReached = "x-esi-error-limited"

    let intHeaderValue defaultValue name =
        HttpRequestResponse.headerValues name
        >> Seq.tryHead
        >> Option.map (Strings.toInt defaultValue)
        >> Option.defaultValue defaultValue

    let errorsRemaining = intHeaderValue 0 errorLimitRemainHeader

    let errorsResetWait =
        intHeaderValue 60 errorLimitResetHeader >> TimeSpan.FromSeconds

    let rec getEsiApiIterate (config: AppConfiguration) (hc: IExternalHttpClient) (log: ILogger<_>) (state: EsiErrorThrottling) count url =
        let errorLimit = config.EsiMinimumErrorLimit()
        
        task {
            try
                let now = DateTime.UtcNow

                $"GET [{url}] iteration #{count}..." |> log.LogTrace
                let! resp = hc.GetAsync url

                $"GET {HttpRequestResponse.loggable resp} received from [{url}]." |> log.LogTrace

                let state =
                    { state with
                        errorLimitRemaining = errorsRemaining resp
                        errorLimitReset = now.Add(errorsResetWait resp) }

                return!
                    match resp with
                    | HttpBadGatewayResponse(_) -> getEsiApiIterate config hc log state (count - 1) url
                    | _ when state.errorLimitRemaining <= errorLimit ->
                        $"{state.errorLimitRemaining} received... breaking circuit" |> log.LogWarning
                        (state, HttpTooManyRequestsResponse([])) |> Threading.toTaskResult
                    | _ -> (state, resp) |> Threading.toTaskResult
            with ex ->
                log.LogError(ex.Message, ex)
                return (state, HttpErrorRequestResponse(Net.HttpStatusCode.InternalServerError, "", []))
        }

    let getEsiApi (config: AppConfiguration) (hc: IExternalHttpClient) (log: ILogger<_>) (state: EsiErrorThrottling) (route: string) =
        let errorLimit = config.EsiMinimumErrorLimit()
        let retryCount = config.EsiRetryCount()

        if
            state.errorLimitRemaining <= errorLimit
            && DateTime.UtcNow < state.errorLimitReset
        then
            $"{state.errorLimitRemaining} received... breaking circuit" |> log.LogWarning
            (state, HttpTooManyRequestsResponse([])) |> Threading.toTaskResult
        else
            $"{config.esiApiUrl}{route}" |> getEsiApiIterate config hc log state retryCount