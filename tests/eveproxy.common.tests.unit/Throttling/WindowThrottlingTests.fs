namespace eveproxy.common.tests.unit.Throttling

open System
open eveproxy
open FsCheck
open FsCheck.Xunit

module WindowThrottlingTests =

    let date = new DateOnly(2010, 12, 31)
    let dateTime time = new DateTime(date, time)

    [<Property(Verbose = true)>]
    let ``windowThrottling accepts only times in a specific window`` (window: int) =
        let currentTime = new TimeOnly(0, 0, 0) |> dateTime

        let f = fun () -> Throttling.windowThrottling Map.empty window 10 currentTime

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
            Throttling.windowThrottling Map.empty 30 count.Get currentTime |> ignore
            false
        with ex -> true

    [<Property(MaxTest = 1)>]
    let ``windowThrottling on empty counts returns no wait`` () =
        let counts = Map.empty
        let currentTime = new TimeOnly(0, 0, 0) |> dateTime

        let (c, wait) = Throttling.windowThrottling counts 30 10 currentTime

        wait = TimeSpan.Zero && c |> Map.count = 1

    [<Property(MaxTest = 1)>]
    let ``windowThrottling on single counts returns no wait`` () =
        let currentTime = new TimeOnly(0, 0, 0) |> dateTime
        let counts = Map.empty |> Map.add currentTime 1

        let (c, wait) = Throttling.windowThrottling counts 30 10 currentTime

        wait = TimeSpan.Zero && c |> Map.count = 1

    [<Property(Verbose = true)>]
    let ``windowThrottling on max counts returns wait`` () =
        let window = 30
        let counts = Gen.elements [ 1..10 ] |> Arb.fromGen

        Prop.forAll counts (fun maxCount ->
            let currentTime = new TimeOnly(0, 0, 0) |> dateTime
            let counts = Map.empty |> Map.add currentTime maxCount

            let (c, wait) = Throttling.windowThrottling counts window maxCount currentTime

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

            let (c, wait) = Throttling.windowThrottling counts window maxCount currentTime

            let delta = window - sec
            wait = TimeSpan.FromSeconds delta)
