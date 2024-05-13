namespace eveproxy.esi

open System
open System.Threading.Tasks
open eveproxy

type IEsiApiPassthroughActor =
    inherit IActor
    abstract member Get: url: string -> Task<HttpRequestResponse>
