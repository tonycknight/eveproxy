﻿namespace eveproxy.zkb.tests.unit.KillmailStorage

open System.Threading.Tasks
open FsCheck
open FsCheck.Xunit
open eveproxy.zkb
open Microsoft.Extensions.Logging
open NSubstitute

module MemoryKillmailReferenceQueueTests =

    [<Property(Verbose = true)>]
    let ``PushAsync PullAsync are symmetric`` (ids: NonEmptyString[]) =
        let config = eveproxy.AppConfiguration.emptyConfig
        let logger = Substitute.For<ILoggerFactory>()

        let ids = ids |> Array.map (fun id -> id.Get)

        let values =
            ids
            |> Array.map (fun id ->
                { KillPackageReferenceData.killmailId = id
                  _id = eveproxy.MongoBson.id ()
                  created = System.DateTime.UtcNow })

        let queue =
            new MemoryKillmailReferenceQueue(config, logger, "") :> IKillmailReferenceQueue

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

    [<Property(Verbose = true)>]
    let ``GetCountAsync - returns count`` (ids: NonEmptyString[]) =
        let config = eveproxy.AppConfiguration.emptyConfig
        let logger = Substitute.For<ILoggerFactory>()

        let queue =
            new MemoryKillmailReferenceQueue(config, logger, "") :> IKillmailReferenceQueue

        let ids = ids |> Array.map (fun id -> id.Get)

        let values =
            ids
            |> Array.map (fun id ->
                { KillPackageReferenceData.killmailId = id
                  _id = eveproxy.MongoBson.id ()
                  created = System.DateTime.UtcNow })

        // Push
        let pushTasks = values |> Array.map queue.PushAsync
        let _ = (pushTasks |> Task.WhenAll).ConfigureAwait(false).GetAwaiter().GetResult()


        let count = queue.GetCountAsync().ConfigureAwait(false).GetAwaiter().GetResult()

        count = ids.Length

    [<Property(Verbose = true)>]
    let ``Clear - clears queue`` (ids: NonEmptyString[]) =
        let config = eveproxy.AppConfiguration.emptyConfig
        let logger = Substitute.For<ILoggerFactory>()

        let queue =
            new MemoryKillmailReferenceQueue(config, logger, "") :> IKillmailReferenceQueue

        let ids = ids |> Array.map (fun id -> id.Get)

        let values =
            ids
            |> Array.map (fun id ->
                { KillPackageReferenceData.killmailId = id
                  _id = eveproxy.MongoBson.id ()
                  created = System.DateTime.UtcNow })

        // Push
        let pushTasks = values |> Array.map queue.PushAsync
        let _ = (pushTasks |> Task.WhenAll).ConfigureAwait(false).GetAwaiter().GetResult()

        // Clear
        queue.ClearAsync().ConfigureAwait(false).GetAwaiter().GetResult()


        let count = queue.GetCountAsync().ConfigureAwait(false).GetAwaiter().GetResult()

        let nextFromQueue = queue.PullAsync().ConfigureAwait(false).GetAwaiter().GetResult()

        count = 0 && nextFromQueue = None
