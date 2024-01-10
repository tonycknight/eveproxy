namespace eveproxy.zkb.tests.unit.KillmailStorage

open System
open FsCheck
open FsCheck.Xunit
open eveproxy.zkb
open Microsoft.Extensions.Logging
open NSubstitute

module KillmailReaderTests =

    [<Property>]
    let ``ReadAsync with unknown ID returns None`` (id: Guid) =
        let id = id.ToString()

        let logger = Substitute.For<ILoggerFactory>()
        let repo = new MemoryKillmailRepository()

        let reader = new KillmailReader(logger, repo) :> IKillmailReader

        let result = reader.ReadAsync(id).Result

        result = None

    [<Property>]
    let ``ReadAsync with known ID returns killmail`` (id: Guid) =
        let id = id.ToString()
        let kp = Utils.kill id

        let logger = Substitute.For<ILoggerFactory>()
        let repo = new MemoryKillmailRepository() :> IKillmailRepository
        let kpo = (repo.SetAsync kp).Result

        let reader = new KillmailReader(logger, repo) :> IKillmailReader

        let result = reader.ReadAsync(id).Result

        kp = (result |> Option.get)
