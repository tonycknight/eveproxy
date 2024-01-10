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

type ActorMessage =
    | Stop
    | Start
    | RouteFetch of url: string * count: int
    | Entity of entity: obj
    | Pull of url: string
    | PullReply of url: string * rc: AsyncReplyChannel<obj>
    | ChildStats of rc: AsyncReplyChannel<ActorStats[]>

type IActor =
    abstract member Post: ActorMessage -> unit
    abstract member GetStats: unit -> Task<ActorStats>
