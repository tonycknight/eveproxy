namespace eveproxy.zkb.tests.unit.KillmailStorage

open FsCheck
open FsCheck.Xunit
open eveproxy.zkb

module KillmailReferenceQueueFactoryTests =

    [<Property>]
    let ``Create with names returns queue`` (name: NonEmptyString) =
        let fact =
            new KillmailReferenceQueueFactory<MemoryKillmailReferenceQueue>() :> IKillmailReferenceQueueFactory

        let result = fact.Create name.Get

        let q = (result :?> MemoryKillmailReferenceQueue)

        q.GetType() = typeof<MemoryKillmailReferenceQueue> && result.Name = name.Get
