namespace eveproxy

[<CLIMutable>]
type RouteStatistics = { route: string; count: int64 }

[<CLIMutable>]
type ProxyStatistics = { routes: RouteStatistics list }
