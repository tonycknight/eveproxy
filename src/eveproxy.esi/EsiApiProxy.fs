namespace eveproxy.esi

open System
open eveproxy
open Microsoft.Extensions.Caching.Memory
open Microsoft.Extensions.Logging

type EsiApiProxy
    (
        cache: IMemoryCache,
        actor: IEsiApiPassthroughActor,
        config: AppConfiguration,
        hc: IExternalHttpClient,
        logFactory: ILoggerFactory
    ) =

    let log = logFactory.CreateLogger<EsiApiProxy>()

    let mutable throttling =
        { EsiErrorThrottling.errorLimitReset = DateTime.UtcNow
          errorLimitRemaining = 100 }

    let expiresHeaderValue defaultValue =
        HttpRequestResponse.headerValues "expires"
        >> Seq.tryHead
        >> Option.map DateTime.Parse
        >> Option.defaultValue defaultValue

    let defaultExpiry () =
        DateTime.UtcNow + TimeSpan.FromMinutes 1.

    let cacheOptions (expiry: DateTime) =
        let options = new MemoryCacheEntryOptions()
        options.AbsoluteExpiration <- expiry
        options

    let cacheKey =
        let prefix = typeof<EsiApiProxy>.Name
        fun (route: string) -> $"{prefix}:{route}"

    let getCache id =
        match cacheKey id |> cache.TryGetValue with
        | true, x -> x :?> HttpRequestResponse |> Some
        | _ -> None

    let setCacheAsync (id: string, expiry, resp: HttpRequestResponse) =
        let key = cacheKey id
        let opts = cacheOptions expiry
        cache.Set(key, resp, opts)

    let getFromEsi = Esi.getEsiApi config hc log

    let get route =
        match getCache route with
        | Some r -> task { return r }
        | None ->
            task {

                let! (t, r) = getFromEsi throttling route

                throttling <- t // On the basis that reference assignments are atomic operations, and we can afford a little skew.

                return
                    match r with
                    | HttpBadGatewayResponse _
                    | HttpExceptionRequestResponse _ -> r
                    | _ ->
                        let expiry = DateTime.UtcNow.AddMinutes(5.)
                        setCacheAsync (route, expiry, r)
            }

    member this.Get route = get route
