namespace eveproxy

open System
open Giraffe

module WebApp =

    let favicon =
        GET
        >=> route "/favicon.ico"
        >=> ResponseCaching.publicResponseCaching 999999 None
        >=> Successful.NO_CONTENT

    let heartbeat =
        GET
        >=> route "/heartbeat"
        >=> ResponseCaching.noResponseCaching
        >=> json [ "OK" ]

    let webApp (sp: IServiceProvider) =
        Api.isAuthorised sp
        >=> choose
                [ favicon
                  GET
                  >=> subRouteCi
                          "/api"
                          (choose
                              [ choose
                                    [ heartbeat
                                      eveproxy.zkb.Api.redisqWebRoutes ()
                                      eveproxy.zkb.Api.zkbWebRoutes () ] ]) ]
