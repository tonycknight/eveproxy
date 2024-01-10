namespace eveproxy

open System
open Giraffe
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Microsoft.Extensions.DependencyInjection

type Startup() =
    member _.ConfigureServices(services: IServiceCollection) =
        services |> ApiStartup.addApi |> zkb.ApiStartup.addServices |> ignore

    member _.Configure (app: IApplicationBuilder) (env: IHostEnvironment) (loggerFactory: ILoggerFactory) =
        app.UseHttpLogging().UseGiraffe(WebApp.webApp app.ApplicationServices)


module Program =

    [<EntryPoint>]
    let main _ =
        let host =
            Host
                .CreateDefaultBuilder()
                .ConfigureWebHostDefaults(fun whb ->
                    whb
                        .UseStartup<Startup>()
                        .UseUrls($"http://+:{8080}")
                        .ConfigureAppConfiguration(ApiStartup.configureAppConfig)
                    |> ignore)

                .Build()

        host.Services |> eveproxy.zkb.ApiStartup.start

        host.Run()
        0
