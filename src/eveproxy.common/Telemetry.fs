﻿namespace eveproxy

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

    abstract member SuccessfulEvewhoRequest: int -> unit
    abstract member ThrottledEvewhoRequest: int -> unit
    abstract member FailedEvewhoRequest: int -> unit

    abstract member SuccessfulZkbRequest: int -> unit
    abstract member ThrottledZkbRequest: int -> unit
    abstract member FailedZkbRequest: int -> unit

    abstract member RedisqProxyRequest: int -> unit
    abstract member EsiProxyRequest: int -> unit
    abstract member ZkbProxyRequest: int -> unit
    abstract member EvewhoProxyRequest: int -> unit

type MetricsTelemetry(meterFactory: IMeterFactory) =
    
    let ingestedKillmailMeter = meterFactory.Create("eveproxy_killmails")
    let createKillmailCounter name = 
        ingestedKillmailMeter.CreateCounter<int>($"eveproxy_killmails_{name}", "Killmail")
    let killmailBadCounter = createKillmailCounter "bad"
    let killmailReceivedCounter = createKillmailCounter "received"
    let killmailIngestedCounter = createKillmailCounter "ingested"

    let proxyRequestMeter = meterFactory.Create("eveproxy_proxy_request")
    let createProxyRequestCounter name = proxyRequestMeter.CreateCounter<int>($"eveproxy_proxy_request_{name}", "Request")
    let redisqProxyRequestCounter = createProxyRequestCounter "redisq"
    let esiProxyRequestCounter = createProxyRequestCounter "esi"
    let zkbProxyRequestCounter = createProxyRequestCounter "zkb"
    let evewhoProxyRequestCounter = createProxyRequestCounter "evewho"

    let esiRequestMeter = meterFactory.Create("eveproxy_request_esi")
    let createEsiCounter name = 
        esiRequestMeter.CreateCounter<int>($"eveproxy_request_esi_{name}", "Request")
    let successfulEsiRequestCounter = createEsiCounter "ok"
    let failedEsiRequestCounter = createEsiCounter "failed"
    let throttledEsiRequestCounter = createEsiCounter "throttled"

    let evewhoRequestMeter = meterFactory.Create("eveproxy_request_evewho")
    let createEvewhoCounter name = 
        evewhoRequestMeter.CreateCounter<int>($"eveproxy_request_evewho_{name}", "Request")
    let successfulEvewhoRequestCounter = createEvewhoCounter "ok"
    let failedEvewhoRequestCounter = createEvewhoCounter "failed"
    let throttledEvewhoRequestCounter = createEvewhoCounter "throttled"

    let zkbRequestMeter = meterFactory.Create("eveproxy_request_zkb")
    let createZkbCounter name = 
        zkbRequestMeter.CreateCounter<int>($"eveproxy_request_zkb_{name}", "Request")
    let successfulZkbRequestCounter = createZkbCounter "ok"
    let failedZkbRequestCounter = createZkbCounter "failed"
    let throttledZkbRequestCounter = createZkbCounter "throttled"

    interface IMetricsTelemetry with
        member this.Dispose () = 
            ingestedKillmailMeter.Dispose()
            esiRequestMeter.Dispose()
            evewhoRequestMeter.Dispose()
            zkbRequestMeter.Dispose()
            proxyRequestMeter.Dispose()
            
        member this.BadKillmails count = killmailBadCounter.Add 1

        member this.ReceivedKillmails count = killmailReceivedCounter.Add count

        member this.IngestedKillmails count = killmailIngestedCounter.Add count

        member this.SuccessfulEsiRequest count = successfulEsiRequestCounter.Add count

        member this.FailedEsiRequest count = failedEsiRequestCounter.Add count

        member this.ThrottledEsiRequest count = throttledEsiRequestCounter.Add count

        member this.SuccessfulEvewhoRequest count = successfulEvewhoRequestCounter.Add count

        member this.FailedEvewhoRequest count = failedEvewhoRequestCounter.Add count

        member this.ThrottledEvewhoRequest count = throttledEvewhoRequestCounter.Add count

        member this.SuccessfulZkbRequest count = successfulZkbRequestCounter.Add count

        member this.FailedZkbRequest count = failedZkbRequestCounter.Add count

        member this.ThrottledZkbRequest count = throttledZkbRequestCounter.Add count

        member this.RedisqProxyRequest count = redisqProxyRequestCounter.Add count

        member this.EsiProxyRequest count = esiProxyRequestCounter.Add count

        member this.EvewhoProxyRequest count = evewhoProxyRequestCounter.Add count

        member this.ZkbProxyRequest count = zkbProxyRequestCounter.Add count