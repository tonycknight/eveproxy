namespace eveproxy

open System
open Microsoft.Extensions.DependencyInjection

module ApiStartup =

    let addMicrobroker (services: IServiceCollection) =
        let config (sp: IServiceProvider) =
            let appConfig = sp.GetRequiredService<AppConfiguration>()

            { Microbroker.Client.MicrobrokerConfiguration.brokerBaseUrl = appConfig.brokerBaseUrl
              Microbroker.Client.MicrobrokerConfiguration.throttleMaxTime = TimeSpan.FromSeconds 5. } 
        
        services
        |> Microbroker.Client.DependencyInjection.addConfiguration config
        |> Microbroker.Client.DependencyInjection.addServices
        