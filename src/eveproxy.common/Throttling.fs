namespace eveproxy

open System

type WindowThrottlingCounts = Map<DateTime, int>

module Throttling =

    let windowThrottling (counts: WindowThrottlingCounts) (window: int) (maxCount: int) (current: DateTime) =
        if window < 1 || window > 60 then
            invalidArg "window" "window out of range."

        if 60 % window > 0 then
            invalidArg "window" "window must be a divisor of 60."

        if maxCount < 1 then
            invalidArg "maxCount" "maxCount out of range."

        // put current time into the window
        let secs = int current.TimeOfDay.Seconds
        let bucket = (int (secs / window)) * window

        let key =
            new DateTime(current.Year, current.Month, current.Day, current.Hour, current.Minute, bucket)

        match counts |> Map.tryFind key with
        | None ->
            let map = Map.empty |> Map.add key 1
            (map, TimeSpan.Zero)
        | Some x when (x + 1 <= maxCount) ->
            let map = counts |> Map.add key (x + 1)
            (map, TimeSpan.Zero)
        | Some x ->
            let wait = window - secs |> TimeSpan.FromSeconds
            (counts, wait)
