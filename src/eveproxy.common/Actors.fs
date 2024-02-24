namespace eveproxy

open System
open System.Threading.Tasks

type ActorStats =
    { name: string
      queueCount: int
      childStats: ActorStats list }

    static member statsName (parent) (name: string) =
        if String.IsNullOrEmpty name then
            $"{parent.GetType().FullName}"
        else
            $"{parent.GetType().FullName}:{name}"

type StorageStats = { name: string; count: int64 }

type ActorMessage =
    | Stop
    | Start
    | ScheduledMaintenance
    | Destroy of name: string
    | RouteFetch of url: string * count: int
    | Entity of entity: obj
    | Pull of url: string
    | PullReply of url: string * rc: AsyncReplyChannel<obj>
    | ChildStats of rc: AsyncReplyChannel<ActorStats[]>
    | LastUpdate of rc: AsyncReplyChannel<DateTime>
    | StorageStats of rc: AsyncReplyChannel<StorageStats[]>

type IActor =
    abstract member Post: ActorMessage -> unit
    abstract member GetStats: unit -> Task<ActorStats>


type IStatsActor =
    inherit IActor
    abstract member GetApiStats: unit -> Task<(ApiRouteStatistics)>