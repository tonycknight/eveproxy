namespace eveproxy.esi

open Microsoft.Extensions.DependencyInjection

module ApiStartup =

    let addServices (sc: IServiceCollection) =
        sc.AddSingleton<IEsiApiPassthroughActor, EsiApiPassthroughActor>()
          .AddSingleton<EsiApiProxy>()
