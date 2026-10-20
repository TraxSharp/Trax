---
title: DeployManifest
description: Reference for --trax-export-deploy and the DeployManifest a host writes with it, which trax deploy reads to build the host's infrastructure.
type: reference
area: data
parent: Configuration
grand_parent: SDK Reference
nav_order: 17
package: Trax.Effect
namespace: Trax.Effect.Configuration.Deploy
source:
  - Trax.Effect/src/Trax.Effect/Configuration/Deploy/DeployManifest.cs
  - Trax.Effect/src/Trax.Effect/Configuration/Deploy/TraxDeployExport.cs
---

# DeployManifest

What a host needs from where it is deployed, as it reports it when started with `--trax-export-deploy <path>`: its
roles, its database, the transports it connects to, the endpoints a load balancer routes to, and the authentication
schemes it registers. `trax deploy` reads it, so the infrastructure it builds follows the host's code.

## Signatures

```csharp fragment
public static class TraxDeployExport
{
    public const string Argument = "--trax-export-deploy";
    public static string? Path { get; }
    public static bool Requested { get; }
    public static string? PathIn(IReadOnlyList<string> args);
}

public sealed class DeployManifest
{
    public const int CurrentVersion = 1;
    public int Version { get; set; }
    public List<string> Roles { get; set; }
    public DeployDatabase? Database { get; set; }
    public List<string> Transports { get; set; }
    public DeployEndpoints Endpoints { get; set; }
    public List<DeployAuthenticationScheme> Authentication { get; set; }
    public string Write();
    public static DeployManifest Read(string json);
}
```

## Properties

| Property | What it holds |
|---|---|
| `Version` | The format version, `1`. `Read` refuses a newer one. |
| `Roles` | Sorted: `api` when the host maps the Trax GraphQL endpoint with `UseTraxGraphQL`, `scheduler` for `AddScheduler`, `worker` for `AddTraxWorker`. |
| `Database` | `DeployDatabase`: `Provider` is `postgres`, `sqlite` or `in-memory`; `MigratesOnStart` is false when the code calls `SkipMigrations()`. Null with no data provider. |
| `Transports` | Sorted: `rabbitmq` when the host broadcasts through RabbitMQ. |
| `Endpoints` | `DeployEndpoints`: the paths of `GraphQL` (`UseTraxGraphQL`), `Health` (`MapHealthChecks`) and `Console` (`UseTraxApiDashboard`), each null when not mapped. |
| `Authentication` | `DeployAuthenticationScheme` per registered scheme, sorted by `Name`, with its `Handler` type name (`JwtBearerHandler`, `OpenIdConnectHandler`, ...). |

## Behavior

`RunTraxAsync`, on a `WebApplication` or any `IHost`, writes the manifest and returns without starting the host when
the process was started with `--trax-export-deploy <path>`. It first runs every startup check the host registers, the
same ones the client export runs, so a host that would refuse to start (demo credentials outside Development, an
operations namespace with no gate) refuses the export too and writes nothing. `UsePostgres` and `UseSqlite` open no
database in an exporting process, and one that calls `app.Run()` instead of `RunTraxAsync` refuses to start.

`TraxDeployExport.Path` is the path after the argument on this process's command line, `Requested` whether there is
one, and `PathIn(args)` the same for any arguments. The argument without a path throws `ArgumentException`.

## Example

```bash
dotnet Orders.dll --trax-export-deploy deploy.json
```

```json
{
  "version": 1,
  "roles": ["api", "scheduler"],
  "database": { "provider": "postgres", "migratesOnStart": true },
  "transports": [],
  "endpoints": { "graphQL": "/trax/graphql", "health": "/trax/health" },
  "authentication": [{ "name": "Bearer", "handler": "JwtBearerHandler" }]
}
```

## See also

- [TraxMigrate](/docs/sdk-reference/configuration/trax-migrate), the deployment step that migrates
- [Client manifest](/docs/sdk-reference/statemachine-api/client-manifest), the other export a host writes
