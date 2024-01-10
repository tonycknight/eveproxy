namespace eveproxy.zkb

open System
open System.Threading.Tasks
open Microsoft.Extensions.Logging
open eveproxy.Threading

type IKillmailRepository =
    abstract member SetAsync: kill: KillPackage -> Task<KillPackage option>
    abstract member GetAsync: id: string -> Task<KillPackage option>

type MemoryKillmailRepository() =
    let cache =
        new System.Collections.Concurrent.ConcurrentDictionary<string, KillPackage>(StringComparer.OrdinalIgnoreCase)

    interface IKillmailRepository with
        member this.SetAsync(kill) =
            let key = KillPackage.killmailId kill

            match key with
            | Some k ->
                cache.[k] <- kill
                kill |> Some |> toTaskResult
            | _ -> None |> toTaskResult

        member this.GetAsync(id) =
            (match cache.TryGetValue id with
             | true, kp -> Some kp
             | _ -> None)
            |> toTaskResult


type IKillmailWriter =
    abstract member WriteAsync: kill: KillPackage -> Task<KillPackage>


type KillmailWriter(logFactory: ILoggerFactory, repo: IKillmailRepository) =
    let log = logFactory.CreateLogger<KillmailWriter>()

    interface IKillmailWriter with
        member this.WriteAsync(kill: KillPackage) =
            task {
                match kill |> KillPackage.killmailId with
                | Some id ->
                    id |> sprintf "--> Writing kill [%s]..." |> log.LogTrace
                    let! kill = repo.SetAsync kill

                    if kill |> Option.isNone then
                        id |> sprintf "--> Kill [%s] not written." |> log.LogTrace
                    else
                        id |> sprintf "--> Written kill [%s]." |> log.LogTrace
                | _ -> "--> Received kill without killmailID - ignoring." |> log.LogTrace

                return kill
            }



type IKillmailReader =
    abstract member ReadAsync: id: string -> Task<KillPackage option>

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
                        id |> sprintf "--> Could not find kill [%s]." |> log.LogTrace
                        None
            }

type IKillmailReferenceQueue =
    abstract member Name: string
    abstract member PushAsync: value: KillPackageReference -> Task
    abstract member PullAsync: unit -> Task<KillPackageReference option>

type MemoryKillmailReferenceQueue(name: string) =
    let kills = new System.Collections.Generic.Queue<KillPackageReference>()

    interface IKillmailReferenceQueue with
        member this.Name = name

        member this.PushAsync(value: KillPackageReference) = task { do kills.Enqueue value }

        member this.PullAsync() =
            task {
                return
                    match kills.TryDequeue() with
                    | (true, p) -> Some p
                    | (false, _) -> None
            }

type IKillmailReferenceQueueFactory =
    abstract member Create: string -> IKillmailReferenceQueue

type KillmailReferenceQueueFactory<'a when 'a :> IKillmailReferenceQueue>() =
    interface IKillmailReferenceQueueFactory with
        member this.Create name =
            let t = typeof<'a>
            Activator.CreateInstance(t, name) :?> IKillmailReferenceQueue
