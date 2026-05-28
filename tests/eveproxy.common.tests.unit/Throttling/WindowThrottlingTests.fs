namespace eveproxy.common.tests.unit.Throttling

open System
open eveproxy
open FsCheck
open FsCheck.FSharp
open FsCheck.Xunit

module WindowThrottlingTests =

    let date = new DateOnly(2010, 12, 31)
    let dateTime time = new DateTime(date, time)

    [<Property(Verbose = true)>]
    let ``windowThrottling accepts only times in a specific window`` (window: int) =
        let currentTime = new TimeOnly(0, 0, 0) |> dateTime
        let throttle = Throttling.windowThrottling (window, 10)

        let f = fun () -> throttle Map.empty currentTime

        if (window < 1 || window > 60) || (60 % window > 0) then
            try
                f () |> ignore
                false
            with ex ->
                true
        else
            f () |> ignore
            true

    [<Property(Verbose = true)>]
    let ``windowThrottling accepts only positive max counts`` (count: NegativeInt) =
        let currentTime = new TimeOnly(0, 0, 0) |> dateTime

        try
            let throttle = Throttling.windowThrottling (30, count.Get)
            throttle Map.empty currentTime |> ignore
            false
        with ex ->
            true

    [<Property(MaxTest = 1)>]
    let ``windowThrottling on empty counts returns no wait`` () =
        let counts = Map.empty
        let currentTime = new TimeOnly(0, 0, 0) |> dateTime
        let throttle = Throttling.windowThrottling (30, 10)

        let (c, wait) = throttle counts currentTime

        wait = TimeSpan.Zero && c |> Map.count = 1

    [<Property(MaxTest = 1)>]
    let ``windowThrottling on single counts returns no wait`` () =
        let currentTime = new TimeOnly(0, 0, 0) |> dateTime
        let counts = Map.empty |> Map.add currentTime 1
        let throttle = Throttling.windowThrottling (30, 10)

        let (c, wait) = throttle counts currentTime

        wait = TimeSpan.Zero && c |> Map.count = 1

    [<Property(Verbose = true)>]
    let ``windowThrottling on max counts returns wait`` () =
        let window = 30L
        let maxCounts = Gen.elements [ 1..10 ] |> Arb.fromGen

        Prop.forAll maxCounts (fun maxCount ->
            let currentTime = new TimeOnly(0, 0, 0) |> dateTime
            let counts = Map.empty |> Map.add currentTime maxCount
            let throttle = Throttling.windowThrottling (30, maxCount)

            let (c, wait) = throttle counts currentTime

            wait = TimeSpan.FromSeconds window)


    [<Property(Verbose = true)>]
    let ``windowThrottling breaching max counts returns variable wait`` () =
        let window = 30
        let secs = Gen.elements [ 0..window ] |> Arb.fromGen

        Prop.forAll secs (fun sec ->
            let maxCount = 10

            let currentTime = new TimeOnly(0, 0, sec) |> dateTime
            let key = new TimeOnly(0, 0, 0) |> dateTime
            let counts = Map.empty |> Map.add key maxCount
            let throttle = Throttling.windowThrottling (window, maxCount)

            let (c, wait) = throttle counts currentTime

            let delta = window - sec |> int64
            wait = TimeSpan.FromSeconds delta)

    [<Property(Verbose = true)>]
    let ``windowThrottling breaching max counts returns variable wait with second bucket`` () =
        let window = 30
        let secs = Gen.elements [ window..59 ] |> Arb.fromGen
        let deltaMultiplier = 60 / window

        Prop.forAll secs (fun sec ->
            let maxCount = 10

            let currentTime = new TimeOnly(0, 0, sec) |> dateTime
            let key = new TimeOnly(0, 0, window) |> dateTime
            let counts = Map.empty |> Map.add key maxCount
            let throttle = Throttling.windowThrottling (window, maxCount)

            let (c, wait) = throttle counts currentTime

            let delta = (window * deltaMultiplier) - sec |> int64
            wait = TimeSpan.FromSeconds delta)
