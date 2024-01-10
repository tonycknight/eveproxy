namespace eveproxy.zkb

open eveproxy
open Newtonsoft.Json.Linq

[<CLIMutable>]
type KillPackageReference = { id: string }

[<CLIMutable>]
type KillPackage =
    { package: obj }

    static member empty = { KillPackage.package = null }

    static member killmail(value: KillPackage) =
        match value.package |> Option.nullToOption with
        | Some package when (package :? JObject) -> (package :?> JObject) |> Some
        | _ -> None

    static member killmailId =
        KillPackage.killmail
        >> Option.map (fun (value: JObject) -> value.Value<string>("killID"))
        >> (fun x ->
            match x with
            | None -> None
            | Some x when x = null -> None
            | Some x -> Some x)

type ReceivedKills = { count: int64 }

type WrittenKills = { count: int64 }

type DistributedKills = { session: string; count: int64 }

type IngestionStats =
    { receivedKills: int64
      writtenKills: int64 }

type DistributionStats =
    { totalDistributedKills: int64
      sessionDistributedKills: Map<string, int64> }

type ApiStats =
    { ingestion: IngestionStats
      distribution: DistributionStats
      routes: Map<string, RouteStatistics> }
