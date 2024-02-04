namespace eveproxy.zkb.tests.unit.KillmailStorage

open System
open FsCheck
open FsCheck.Xunit
open eveproxy.zkb
open Newtonsoft.Json.Linq

module MemoryKillmailRepositoryTests =

    [<Property>]
    let ``SetAsync with invalid ID returns null`` (k: Guid) =
        let kp = $" {{ killID2: '{k}', testdata: {{}} }}" |> Utils.killFromJson

        let repo = new MemoryKillmailRepository() :> IKillmailRepository

        let result = repo.SetAsync kp

        result.Result = None


    [<Property>]
    let ``SetAsync with valid ID returns kill`` (k: Guid) =
        let kp = Utils.kill k

        let repo = new MemoryKillmailRepository() :> IKillmailRepository

        let result = repo.SetAsync kp

        result.Result = Some kp


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
    let ``GetCountAsync on empty repo returns zero``()=
        let repo = new MemoryKillmailRepository() :> IKillmailRepository
        let result = (repo.GetCountAsync()).Result

        result = 0

    [<Property>]
    let ``GetCountAsync reflects stored count``(count: PositiveInt)=
        
        let repo = new MemoryKillmailRepository() :> IKillmailRepository

        for x in [ 1 .. count.Get ] do
            let kp = Guid.NewGuid() |> Utils.kill 
            (repo.SetAsync kp).Result |> ignore            

        let result = (repo.GetCountAsync()).Result

        result = count.Get
