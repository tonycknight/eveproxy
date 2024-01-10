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

        let kill = { KillPackage.package = None }

        let result = writer.WriteAsync(kill).Result

        result = KillPackage.empty


    [<Property>]
    let ``WriteAsync with kill returns kill`` (id: Guid) =
        let logger = Substitute.For<ILoggerFactory>()
        let repo = new MemoryKillmailRepository()

        let writer = new KillmailWriter(logger, repo) :> IKillmailWriter

        let kill = Utils.kill id

        let result = writer.WriteAsync(kill).Result

        result = kill
