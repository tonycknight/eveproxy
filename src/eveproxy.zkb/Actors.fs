namespace eveproxy.zkb

open System
open System.Threading.Tasks
open eveproxy

type IRedisqIngestionActor =
    inherit IActor

type IKillWriteActor =
    inherit IActor

type IApiStatsActor =
    inherit IActor
    abstract member GetApiStats: unit -> Task<(ApiStats)>

type ISessionActor =
    inherit IActor
    abstract member GetNext: unit -> Task<KillPackage>
    abstract member GetLastPullTime: unit -> Task<DateTime>

type ISessionsActor =
    inherit IActor
    abstract member GetNext: name: string -> Task<KillPackage>
