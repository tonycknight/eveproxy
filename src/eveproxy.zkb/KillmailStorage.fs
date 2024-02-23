namespace eveproxy.zkb

open System
open System.Diagnostics.CodeAnalysis
open System.Threading.Tasks
open Microsoft.Extensions.Logging
open eveproxy.Threading

type KillmailWriteResult =
    | Noop
    | Inserted of killmail: KillPackageData
    | Updated of killmail: KillPackageData

type IKillmailRepository =
    abstract member SetAsync: kill: KillPackageData -> Task<KillmailWriteResult>
    abstract member GetAsync: id: string -> Task<KillPackageData option>
    abstract member GetCountAsync: unit -> Task<int64>

type MemoryKillmailRepository() =
    let cache =
        new System.Collections.Concurrent.ConcurrentDictionary<string, KillPackageData>(
            StringComparer.OrdinalIgnoreCase
        )

    interface IKillmailRepository with
        member this.SetAsync(kill) =
            let key = KillPackageData.killmailId kill

            match key with
            | Some k ->
                if cache.ContainsKey k then
                    cache.[k] <- kill
                    KillmailWriteResult.Updated kill |> toTaskResult
                else
                    cache.[k] <- kill
                    KillmailWriteResult.Inserted kill |> toTaskResult
            | _ -> KillmailWriteResult.Noop |> toTaskResult

        member this.GetAsync(id) =
            (match cache.TryGetValue id with
             | true, kp -> Some kp
             | _ -> None)
            |> toTaskResult

        member this.GetCountAsync() = task { return cache.Count }

[<ExcludeFromCodeCoverage>]
type MongoKillmailRepository(config: eveproxy.AppConfiguration) =

    [<Literal>]
    let collectionName = "killmails"

    let mongoCol =
        eveproxy.Mongo.initCollection "" config.mongoDbName collectionName config.mongoConnection

    let setId id (kill: KillPackageData) =
        if Object.ReferenceEquals(kill._id, null) then
            kill._id <- id

        kill

    let getAsync id =
        sprintf "{'_id': '%s' }" id
        |> eveproxy.Mongo.getSingle<KillPackageData> mongoCol

    let setAsync (id: string, kill: KillPackageData) =
        task {
            let! r =
                kill
                |> setId id
                |> eveproxy.MongoBson.ofObject
                |> eveproxy.Mongo.upsert mongoCol

            return
                match r.MatchedCount, r.ModifiedCount with
                | 0L, _ when Object.ReferenceEquals(r.UpsertedId, null) |> not -> KillmailWriteResult.Inserted kill
                | x, y when x > 0 && y > 0 -> KillmailWriteResult.Updated kill
                | _, _ -> KillmailWriteResult.Noop
        }

    interface IKillmailRepository with
        member this.SetAsync(kill) =
            match KillPackageData.killmailId kill with
            | Some id -> setAsync (id, kill)
            | None -> task { return KillmailWriteResult.Noop }


        member this.GetAsync(id) = getAsync id

        member this.GetCountAsync() = eveproxy.Mongo.count mongoCol

type IKillmailWriter =
    abstract member WriteAsync: kill: KillPackageData -> Task<KillmailWriteResult>


type KillmailWriter(logFactory: ILoggerFactory, repo: IKillmailRepository) =
    let log = logFactory.CreateLogger<KillmailWriter>()

    interface IKillmailWriter with
        member this.WriteAsync(kill: KillPackageData) =

            match kill |> KillPackageData.killmailId with
            | Some id ->
                task {
                    id |> sprintf "--> Writing kill [%s]..." |> log.LogTrace
                    let! kr = repo.SetAsync kill
                    
                    match kr with
                    | KillmailWriteResult.Noop -> id |> sprintf "--> Kill [%s] not written." |> log.LogWarning
                    | KillmailWriteResult.Inserted k -> id |> sprintf "--> Inserted kill [%s]." |> log.LogTrace
                    | KillmailWriteResult.Updated k -> id |> sprintf "--> Updated kill [%s]." |> log.LogTrace

                    return kr
                }
            | _ ->
                task {
                    "--> Received kill without killmailID - ignoring." |> log.LogWarning
                    return KillmailWriteResult.Noop
                }





type IKillmailReader =
    abstract member ReadAsync: id: string -> Task<KillPackageData option>

type KillmailReader(logFactory: ILoggerFactory, repo: IKillmailRepository) =
    let log = logFactory.CreateLogger<KillmailReader>()

    interface IKillmailReader with
        member this.ReadAsync(id: string) =
            task {
                id |> sprintf "--> Fetching kill [%s]..." |> log.LogTrace
                let! r = repo.GetAsync id

                return
                    match r with
                    | Some r ->
                        id |> sprintf "--> Fetched kill [%s]." |> log.LogTrace
                        Some r
                    | _ ->
                        id |> sprintf "--> Could not find kill [%s]." |> log.LogWarning
                        None
            }

type IKillmailReferenceQueue =
    abstract member Name: string
    abstract member PushAsync: value: KillPackageReferenceData -> Task
    abstract member PullAsync: unit -> Task<KillPackageReferenceData option>
    abstract member ClearAsync: unit -> Task
    abstract member GetCountAsync: unit -> Task<int64>

type IKillmailReferenceQueueFinder =
    abstract member GetNames: unit -> string[]

type MemoryKillmailReferenceQueue(config: eveproxy.AppConfiguration, logFactory: ILoggerFactory, name: string) =
    let kills = new System.Collections.Generic.Queue<KillPackageReferenceData>()

    interface IKillmailReferenceQueue with
        member this.Name = name

        member this.GetCountAsync() = task { return kills.Count }

        member this.PushAsync(value: KillPackageReferenceData) = task { do kills.Enqueue value }

        member this.ClearAsync() = task { do kills.Clear() }

        member this.PullAsync() =
            task {
                return
                    match kills.TryDequeue() with
                    | (true, p) -> Some p
                    | (false, _) -> None
            }

module KillmailReferenceQueues =
    [<Literal>]
    let defaultQueueName = "default"

    [<Literal>]
    let queueNamePrefix = $"killmail_queue__"

[<ExcludeFromCodeCoverage>]
type MongoKillmailReferenceQueue(config: eveproxy.AppConfiguration, logFactory: ILoggerFactory, name: string) =
    let name =
        name |> eveproxy.Strings.defaultIf "" KillmailReferenceQueues.defaultQueueName

    let collectionName = $"{KillmailReferenceQueues.queueNamePrefix}{name}"
    let logger = logFactory.CreateLogger<MongoKillmailReferenceQueue>()

    let mongoCol =
        eveproxy.Mongo.initCollection "" config.mongoDbName collectionName config.mongoConnection

    interface IKillmailReferenceQueue with
        member this.Name = name

        member this.GetCountAsync() = eveproxy.Mongo.count mongoCol

        member this.PushAsync(value: KillPackageReferenceData) =
            task {
                $"Pushing killmail reference [{value.killmailId}] to queue [{name}]..."
                |> logger.LogTrace

                try
                    do! [ value ] |> eveproxy.Mongo.pushToQueue mongoCol

                    $"Pushed killmail reference [{value.killmailId}] to queue [{name}]."
                    |> logger.LogTrace
                with ex ->
                    logger.LogError(ex.Message, ex)
            }

        member this.ClearAsync() =
            task {
                $"Clearing queue [{name}]..." |> logger.LogTrace
                do eveproxy.Mongo.deleteCol mongoCol
            }

        member this.PullAsync() =
            eveproxy.Mongo.pullSingletonFromQueue<KillPackageReferenceData> mongoCol

[<ExcludeFromCodeCoverage>]
type MongoKillmailReferenceQueueFinder(config: eveproxy.AppConfiguration) =
    interface IKillmailReferenceQueueFinder with
        member this.GetNames() =

            let colNames =
                eveproxy.Mongo.findCollectionNames config.mongoDbName config.mongoConnection

            colNames
            |> Array.filter (fun n -> n.StartsWith(KillmailReferenceQueues.queueNamePrefix))
            |> Array.map (fun n -> n.Substring(KillmailReferenceQueues.queueNamePrefix.Length))



type IKillmailReferenceQueueFactory =
    abstract member Create: string -> IKillmailReferenceQueue

type KillmailReferenceQueueFactory<'a when 'a :> IKillmailReferenceQueue>
    (config: eveproxy.AppConfiguration, logFactory: ILoggerFactory) =

    interface IKillmailReferenceQueueFactory with
        member this.Create name =
            let t = typeof<'a>
            let args = [| config :> obj; logFactory :> obj; name :> obj |]
            Activator.CreateInstance(t, args) :?> IKillmailReferenceQueue
