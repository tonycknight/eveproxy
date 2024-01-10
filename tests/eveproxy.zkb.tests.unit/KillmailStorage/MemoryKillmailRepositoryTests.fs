namespace eveproxy.zkb.tests.unit.KillmailStorage

open System
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
