namespace eveproxy.zkb.tests.unit.KillmailStorage

open System
open FsCheck
open FsCheck.Xunit
open eveproxy.zkb

module MemoryKillmailRepositoryTests =

    [<Property>]
    let ``SetAsync with invalid ID returns null`` (k: Guid) =
        let kp = $" {{ killID2: '{k}', testdata: {{}} }}" |> Utils.killFromJson

        let repo = new MemoryKillmailRepository() :> IKillmailRepository

        let result = repo.SetAsync kp

        result.Result = KillmailWriteResult.Noop


    [<Property>]
    let ``SetAsync with valid unknown ID returns kill`` (k: Guid) =
        let kp = Utils.kill k

        let repo = new MemoryKillmailRepository() :> IKillmailRepository

        let result = repo.SetAsync kp

        result.Result = KillmailWriteResult.Inserted kp

    [<Property>]
    let ``SetAsync with valid known ID returns kill`` (k: Guid) =
        let kp = Utils.kill k

        let repo = new MemoryKillmailRepository() :> IKillmailRepository

        let result = repo.SetAsync kp
        let result2 = repo.SetAsync kp

        result.Result = KillmailWriteResult.Inserted kp
        && result2.Result = KillmailWriteResult.Updated kp


    [<Property>]
    let ``GetAsync with unknown ID returns null`` (k: Guid) =

        let repo = new MemoryKillmailRepository() :> IKillmailRepository

        let result = repo.GetAsync(k.ToString())

        result.Result = None

    [<Property>]
    let ``GetAsync with known ID returns kill`` (k: Guid) =
        let kp = Utils.kill k

        let repo = new MemoryKillmailRepository() :> IKillmailRepository

        let _ = (repo.SetAsync kp).Result

        let result = repo.GetAsync(k.ToString())

        result.Result = Some kp

    [<Xunit.Fact>]
    let ``GetCountAsync on empty repo returns zero`` () =
        let repo = new MemoryKillmailRepository() :> IKillmailRepository
        let result = (repo.GetCountAsync()).Result

        result = 0

    [<Property>]
    let ``GetCountAsync reflects stored count`` (count: PositiveInt) =

        let repo = new MemoryKillmailRepository() :> IKillmailRepository

        for x in [ 1 .. count.Get ] do
            let kp = Guid.NewGuid() |> Utils.kill
            (repo.SetAsync kp).Result |> ignore

        let result = (repo.GetCountAsync()).Result

        result = count.Get
