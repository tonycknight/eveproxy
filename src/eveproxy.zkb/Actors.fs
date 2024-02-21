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
    abstract member GetNext: unit -> Task<KillPackageData>
    abstract member GetCreationTime: unit -> DateTime
    abstract member GetLastPullTime: unit -> Task<DateTime>
    abstract member GetStorageStats: unit -> Task<StorageStats[]>

type ISessionsActor =
    inherit IActor
    abstract member GetNext: name: string -> Task<KillPackageData>
    abstract member GetStorageStats: unit -> Task<StorageStats[]>

type IZkbApiPassthroughActor =
    inherit IActor
    abstract member Get: url: string -> Task<HttpRequestResponse>
