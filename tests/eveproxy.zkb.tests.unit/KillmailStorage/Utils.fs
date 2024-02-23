namespace eveproxy.zkb.tests.unit.KillmailStorage

open System
open eveproxy.zkb
open Newtonsoft.Json.Linq

module Utils =

    let killFromJson json =
        { KillPackageData.package = JObject.Parse(json)
          _id = eveproxy.MongoBson.id ()
          created = DateTime.UtcNow }

    let kill (id) =
        $" {{ killID: '{id}', testdata: {{}} }}" |> killFromJson
