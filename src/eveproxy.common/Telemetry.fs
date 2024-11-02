namespace eveproxy

open System
open System.Diagnostics.Metrics

type IMetricsTelemetry =
    inherit IDisposable
    abstract member BadKillmails: int -> unit
    abstract member ReceivedKillmails: int -> unit
    abstract member IngestedKillmails: int -> unit

    abstract member SuccessfulEsiRequest: int -> unit
    abstract member FailedEsiRequest: int -> unit
    abstract member ThrottledEsiRequest: int -> unit

type MetricsTelemetry(meterFactory: IMeterFactory) =
            
    
    let ingestedKillmailMeter = meterFactory.Create("eveproxy_killmails")
    let createKillmailCounter name = 
        ingestedKillmailMeter.CreateCounter<int>($"eveproxy_killmails_{name}", "Killmail")
    let killmailBadCounter = createKillmailCounter "bad"
    let killmailReceivedCounter = createKillmailCounter "received"
    let killmailIngestedCounter = createKillmailCounter "ingested"

    let esiRequestMeter = meterFactory.Create("eveproxy_request_esi")
    let createEsiCounter name = 
        esiRequestMeter.CreateCounter<int>($"eveproxy_request_esi_{name}", "Request")
    let successfulEsiRequestCounter = createEsiCounter "ok"
    let failedEsiRequestCounter = createEsiCounter "failed"
    let throttledEsiRequestCounter = createEsiCounter "throttled"

    interface IMetricsTelemetry with
        member this.Dispose () = 
            ingestedKillmailMeter.Dispose()
            esiRequestMeter.Dispose()
            
        member this.BadKillmails count = killmailBadCounter.Add 1

        member this.ReceivedKillmails count = killmailReceivedCounter.Add count

        member this.IngestedKillmails count = killmailIngestedCounter.Add count

        member this.SuccessfulEsiRequest count = successfulEsiRequestCounter.Add count

        member this.FailedEsiRequest count = failedEsiRequestCounter.Add count

        member this.ThrottledEsiRequest count = throttledEsiRequestCounter.Add count