namespace eveproxy.zkb.tests.unit.KillmailStorage

open eveproxy.zkb
open Newtonsoft.Json.Linq

module Utils =

    let killFromJson json =
        { KillPackage.package = JObject.Parse(json) }

    let kill (id) =
        $" {{ killID: '{id}', testdata: {{}} }}" |> killFromJson
