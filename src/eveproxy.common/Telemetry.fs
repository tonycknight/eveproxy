namespace eveproxy

open System
open System.Diagnostics.Metrics

type IMetricsTelemetry =
    inherit IDisposable
    abstract member ReceivedKillmails: int -> unit

type MetricsTelemetry(meterFactory: IMeterFactory) =
            
    [<Literal>]
    let killmailType = "Killmail"

    let ingestedKillmailMeter = meterFactory.Create("eveproxy_killmails")
    let killmailReceivedMeter = ingestedKillmailMeter.CreateCounter<int>("eveproxy_received_killmails", killmailType)

    interface IMetricsTelemetry with
        member this.Dispose () = 
            ingestedKillmailMeter.Dispose()
            
        member this.ReceivedKillmails count = killmailReceivedMeter.Add count