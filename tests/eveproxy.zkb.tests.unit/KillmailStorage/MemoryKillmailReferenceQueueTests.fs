namespace eveproxy.zkb.tests.unit.KillmailStorage

open System.Threading.Tasks
open FsCheck
open FsCheck.Xunit
open eveproxy.zkb

module MemoryKillmailReferenceQueueTests =

    [<Property(Verbose = true)>]
    let ``PushAsync PullAsync are symmetric`` (ids: NonEmptyString[]) =
        let config = eveproxy.AppConfiguration.emptyConfig
        let ids = ids |> Array.map (fun id -> id.Get)
        let values = ids |> Array.map (fun id -> { KillPackageReferenceData.killmailId = id; _id = eveproxy.MongoBson.id() })

        let queue = new MemoryKillmailReferenceQueue(config, "") :> IKillmailReferenceQueue

        // Push
        let pushTasks = values |> Array.map queue.PushAsync
        let _ = (pushTasks |> Task.WhenAll).ConfigureAwait(false).GetAwaiter().GetResult()

        // Pull until nothing more returned
        let rec pull (results: KillPackageReferenceData list) =
            match queue.PullAsync().ConfigureAwait(false).GetAwaiter().GetResult() with
            | None -> results
            | Some r -> pull (r :: results)

        let pullResults = pull [] |> List.rev

        pullResults = (List.ofArray values)
