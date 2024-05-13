namespace eveproxy.esi

open System
open eveproxy
open Microsoft.Extensions.Caching.Memory

type EsiApiProxy(config: eveproxy.AppConfiguration, cache: IMemoryCache, actor: IEsiApiPassthroughActor) =

    let cacheOptions () =
        let now = DateTime.UtcNow
        let options = new MemoryCacheEntryOptions()
        let expr = now + TimeSpan.FromMinutes 10 // TODO: 
        options.AbsoluteExpiration <- expr
        options

    let cacheKey =
        let prefix = typeof<EsiApiProxy>.Name
        fun (route: string) -> $"{prefix}:{route}"

    let getCache id =
        let key = cacheKey id

        match cache.TryGetValue(key) with
        | true, x -> x :?> HttpRequestResponse |> Some
        | _ -> None

    let setCacheAsync (id: string, resp: HttpRequestResponse) =
        let key = cacheKey id
        let opts = cacheOptions ()
        cache.Set(key, resp, opts)

    let get route =         
        match getCache route with
        | Some r -> task { return r }
        | None ->
            task {
                let! r = actor.Get route
                return setCacheAsync (route, r)
            }
        
    member this.Get route = get route
    

    
