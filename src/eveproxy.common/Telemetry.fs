namespace eveproxy

open System
open System.Diagnostics.Metrics

type IMetricsTelemetry =
    inherit IDisposable
    abstract member BadKillmails: int -> unit
    abstract member ReceivedKillmails: int -> unit
    abstract member IngestedKillmails: int -> unit

type MetricsTelemetry(meterFactory: IMeterFactory) =
            
    [<Literal>]
    let killmailType = "Killmail"

    let ingestedKillmailMeter = meterFactory.Create("eveproxy_killmails")
    let killmailBadCounter = ingestedKillmailMeter.CreateCounter<int>("eveproxy_bad_killmails", killmailType)
    let killmailReceivedCounter = ingestedKillmailMeter.CreateCounter<int>("eveproxy_received_killmails", killmailType)
    let killmailIngestedCounter = ingestedKillmailMeter.CreateCounter<int>("eveproxy_ingested_killmails", killmailType)

    interface IMetricsTelemetry with
        member this.Dispose () = 
            ingestedKillmailMeter.Dispose()
            
        member this.BadKillmails count = killmailBadCounter.Add 1

        member this.ReceivedKillmails count = killmailReceivedCounter.Add count

        member this.IngestedKillmails count = killmailIngestedCounter.Add count