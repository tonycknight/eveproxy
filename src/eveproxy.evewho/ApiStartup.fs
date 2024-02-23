namespace eveproxy.evewho

open Microsoft.Extensions.DependencyInjection

module ApiStartup =
    
    let addServices (sc: IServiceCollection) =
        sc.AddSingleton<IEvewhoApiPassthroughActor, EvewhoApiPassthroughActor>()

