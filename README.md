# eveproxy

[![Build & Release](https://github.com/tonycknight/eveproxy/actions/workflows/build.yml/badge.svg)](https://github.com/tonycknight/eveproxy/actions/workflows/build.yml)

A proxy service for APIs in the Eve online domain.

Some notable examples are:
- [Redisq](https://github.com/zKillboard/RedisQ) - the only live-streaming killmail service in existence.
- [Zkb](https://github.com/zKillboard/zKillboard) - the grand daddy of all killboards
- [Evewho](https://github.com/zKillboard/evewho) - character and corporation lookup

## How to install

A docker image is available [from Github Container Registry](https://github.com/users/tonycknight/packages/container/package/eveproxy).

```
docker pull ghcr.io/tonycknight/eveproxy:<latest tag>
```

You'll also need a MongoDB database installed, available and protected. _Please note that the database is not created nor maintained. See the [MongoDB documentation on how to install and create databases](https://www.mongodb.com/docs/manual/tutorial/getting-started/)_

## How to run

Start the container:

```
docker run -it --rm --publish 8080:8080 eveproxy:<tag> --mongoDbName "<database name>" --mongoConnection "<connection string>" 
```

The parameters you'll need to pass are:

| | |
| - | - |
| `mongoDbName` | The name of the database within the Mongo installation. |
| `mongoConnection` | The connection string to the Mongo DB. | 

### Logging control

Trace telemetry is on by default. The available log levels are `None`, `Trace`, `Debug`, `Information`, `Warning`, `Error` & `Critical`.

To select the log levels you want, specify the log levels as command line arguments. 

Each argument follows this pattern:

`--Logging:Console:LogLevel:<Namespace>=<log level>`

| Namespace | Default log level | | 
| - | - | - | 
| `Microsoft.Hosting.Lifetime` | `Information` | App start up and shutdown |
| `eveproxy` | `Trace` | All telemetry from the `eveproxy` namespace | 
| `Microsoft.AspNetCore.HttpLogging.HttpLoggingMiddleware` | `Information` | HTTP request and response telemetry | 

For example, to show only `eveproxy` warnings:

`--Logging:Console:LogLevel:eveproxy=Warning`


## The endpoints

### API endpoints

| | | 
| - | - |
| `GET /api/stats/` | Get statistics on the API's use. |
| `GET /api/heartbeat/` | Check the API is alive |

### Redisq proxies

| | | 
| - | - |
| `GET /api/redisq/v1/kills/` | Get the next-in-sequence killmail. |
| `GET /api/redisq/v1/kills/id/[id]/` | Get the killmail of `id` if it's been cached. |
| `GET /api/redisq/v1/kills/session/[session]/` | Sessions are analogous to queues. To get a specific stream of kills, just give a unique name for `session`. |

### Zkb API proxies

| | | 
| - | - |
| `GET /api/zkb/v1/[Zkb API route]/` | Send GET traffic to the given `Zkb API route`. For example, use `/api/zkb/v1/losses/solo/` to get solo losses. See [the zkb wiki](https://github.com/zKillboard/zKillboard/wiki) for details. |

### Evewho API proxies

| | | 
| - | - |
| `GET /api/evewho/v1/[evewho API route]/` | Send GET traffic to the given `evewho API route`. For example, use `/api/zkb/v1/character/1633218082` to get details on a character. See [evewho](https://github.com/zKillboard/evewho) for details. |

## Copyright and disclaimers

This API proxies the wonderful work of [cvweiss](https://github.com/cvweiss) & Zkillboard's [Redisq](https://github.com/zKillboard/RedisQ).

All data coming through eveproxy is copyright [CCP hf](https://www.ccpgames.com/). See [CCP](CCP.md).

