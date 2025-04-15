namespace eveproxy.esi

open System
open eveproxy
open Microsoft.Extensions.Logging

type EsiErrorThrottling =
    { errorLimitRemaining: int
      errorLimitReset: DateTime }

module Esi =
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

    let instrumentResponse (log: ILogger<_>) (metrics: IMetricsTelemetry) url resp =
        $"GET {HttpRequestResponse.loggable resp} received from [{url}]."
        |> log.LogTrace

        match resp with
        | HttpOkRequestResponse _ -> metrics.SuccessfulEsiRequest 1
        | HttpTooManyRequestsResponse _ -> metrics.ThrottledEsiRequest 1
        | _ -> metrics.FailedEsiRequest 1

    let rec getEsiApiIterate
        (config: AppConfiguration)
        (hc: IExternalHttpClient)
        (log: ILogger<_>)
        (metrics: IMetricsTelemetry)
        (state: EsiErrorThrottling)
        count
        url
        =
        let errorLimit = config.EsiMinimumErrorLimit()

        task {
            try
                let now = DateTime.UtcNow

                $"GET [{url}] iteration #{count}..." |> log.LogTrace
                let! resp = hc.GetAsync url

                instrumentResponse log metrics url resp

                let state =
                    { state with
                        errorLimitRemaining = errorsRemaining resp
                        errorLimitReset = now.Add(errorsResetWait resp) }

                return!
                    match resp with
                    | HttpBadGatewayResponse(_) -> getEsiApiIterate config hc log metrics state (count - 1) url
                    // TODO: 400s can mean "timeout connecting to Tranqulity" (check body)
                    | _ when state.errorLimitRemaining <= errorLimit ->
                        $"{state.errorLimitRemaining} received... breaking circuit" |> log.LogWarning
                        (state, HttpTooManyRequestsResponse([])) |> Threading.toTaskResult
                    | _ -> (state, resp) |> Threading.toTaskResult
            with ex ->
                log.LogError(ex.Message, ex)
                return (state, HttpErrorRequestResponse(Net.HttpStatusCode.InternalServerError, "", []))
        }

    let getEsiApi
        (config: AppConfiguration)
        (hc: IExternalHttpClient)
        (log: ILogger<_>)
        (metrics: IMetricsTelemetry)
        (state: EsiErrorThrottling)
        (route: string)
        =
        let errorLimit = config.EsiMinimumErrorLimit()
        let retryCount = config.EsiRetryCount()

        if
            state.errorLimitRemaining <= errorLimit
            && DateTime.UtcNow < state.errorLimitReset
        then
            $"{state.errorLimitRemaining} received... breaking circuit" |> log.LogWarning
            (state, HttpTooManyRequestsResponse([])) |> Threading.toTaskResult
        else
            $"{config.esiApiUrl}{route}"
            |> getEsiApiIterate config hc log metrics state retryCount
