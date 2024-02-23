namespace eveproxy.evewho

open System
open System.Threading.Tasks
open eveproxy

type IEvewhoApiPassthroughActor =
    inherit IActor
    abstract member Get: url: string -> Task<HttpRequestResponse>
