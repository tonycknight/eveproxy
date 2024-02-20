namespace eveproxy.zkb.tests.unit.KillmailStorage

open FsCheck
open FsCheck.Xunit
open eveproxy.zkb
open Microsoft.Extensions.Logging
open NSubstitute

module KillmailReferenceQueueFactoryTests =

    [<Property>]
    let ``Create of memory queue with names returns queue`` (name: NonEmptyString) =
        let config = eveproxy.AppConfiguration.emptyConfig
        let logger = Substitute.For<ILoggerFactory>()

        let fact =
            new KillmailReferenceQueueFactory<MemoryKillmailReferenceQueue>(config, logger)
            :> IKillmailReferenceQueueFactory

        let result = fact.Create name.Get

        let q = (result :?> MemoryKillmailReferenceQueue)

        q.GetType() = typeof<MemoryKillmailReferenceQueue> && result.Name = name.Get
