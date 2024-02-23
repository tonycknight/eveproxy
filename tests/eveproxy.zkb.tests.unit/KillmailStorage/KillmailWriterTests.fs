namespace eveproxy.zkb.tests.unit.KillmailStorage

open System
open FsCheck
open FsCheck.Xunit
open eveproxy.zkb
open Microsoft.Extensions.Logging
open NSubstitute

module KillmailWriterTests =

    [<Xunit.Fact>]
    let ``WriteAsync with empty returns empty`` () =
        let logger = Substitute.For<ILoggerFactory>()
        let repo = new MemoryKillmailRepository()

        let writer = new KillmailWriter(logger, repo) :> IKillmailWriter

        let kill =
            { KillPackageData.package = None
              _id = eveproxy.MongoBson.id ()
              created = DateTime.UtcNow }

        let result = writer.WriteAsync(kill).Result

        result = KillmailWriteResult.Noop

    [<Property>]
    let ``WriteAsync with new kill returns kill`` (id: Guid) =
        let logger = Substitute.For<ILoggerFactory>()
        let repo = new MemoryKillmailRepository()

        let writer = new KillmailWriter(logger, repo) :> IKillmailWriter

        let kill = Utils.kill id

        let result = writer.WriteAsync(kill).Result

        result = KillmailWriteResult.Inserted kill

    [<Property>]
    let ``WriteAsync with existing kill returns kill`` (id: Guid) =
        let logger = Substitute.For<ILoggerFactory>()
        let repo = new MemoryKillmailRepository()

        let writer = new KillmailWriter(logger, repo) :> IKillmailWriter

        let kill = Utils.kill id

        let result = writer.WriteAsync(kill).Result
        let result2 = writer.WriteAsync(kill).Result

        result = KillmailWriteResult.Inserted kill
        && result2 = KillmailWriteResult.Updated kill
