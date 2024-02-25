namespace eveproxy

[<CLIMutable>]
type RouteStatistics = { route: string; count: int64 }

[<CLIMutable>]
type ApiRouteStatistics =
    { routes: Map<string, RouteStatistics> }
