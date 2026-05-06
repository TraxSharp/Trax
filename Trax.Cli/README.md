# Trax.Cli

[![Build](https://github.com/TraxSharp/Trax.Cli/actions/workflows/nuget_release.yml/badge.svg)](https://github.com/TraxSharp/Trax.Cli/actions/workflows/nuget_release.yml)
[![NuGet Version](https://img.shields.io/nuget/v/Trax.Cli)](https://www.nuget.org/packages/Trax.Cli/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Trax.Cli)](https://www.nuget.org/packages/Trax.Cli/)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Last Commit](https://img.shields.io/github/last-commit/TraxSharp/Trax.Cli)](https://github.com/TraxSharp/Trax.Cli/commits/main)
[![codecov](https://codecov.io/gh/TraxSharp/Trax.Cli/branch/main/graph/badge.svg)](https://codecov.io/gh/TraxSharp/Trax.Cli)
[![Docs](https://img.shields.io/badge/docs-traxsharp.net-blue)](https://traxsharp.net/docs)

CLI tool for [Trax](https://www.nuget.org/packages/Trax.Effect/). Generates Trax API projects from GraphQL or OpenAPI schemas.

## The Trax Stack

Trax is a layered framework split across several repos. You can stop at whatever layer solves your problem. **You are here: Trax.Cli.**

| Repo | Adds |
|------|------|
| [Trax.Core](https://github.com/TraxSharp/Trax.Core) | Pipelines, junctions, railway error propagation |
| [Trax.Effect](https://github.com/TraxSharp/Trax.Effect) | Execution logging, DI, pluggable storage |
| [Trax.Mediator](https://github.com/TraxSharp/Trax.Mediator) | Decoupled dispatch via `TrainBus` |
| [Trax.Scheduler](https://github.com/TraxSharp/Trax.Scheduler) | Cron schedules, retries, dead-letter queues |
| [Trax.Api](https://github.com/TraxSharp/Trax.Api) | GraphQL API for remote access |
| [Trax.Dashboard](https://github.com/TraxSharp/Trax.Dashboard) | Blazor monitoring UI |
| **[Trax.Cli](https://github.com/TraxSharp/Trax.Cli)** | `trax-cli` project scaffolding tool |
| [Trax.Samples](https://github.com/TraxSharp/Trax.Samples) | Sample apps and a `dotnet new` template |

Full documentation: [traxsharp.net/docs](https://traxsharp.net/docs).

## What This Does

Takes an existing API schema and scaffolds a Trax project with two parts: a hub project (from the `trax-hub` template, with API + Scheduler + Dashboard in one process) and a shared trains library with trains, junctions, input/output records, and models. The trains library follows the same structure as the DistributedWorkers sample and can be referenced by an API, scheduler, or standalone workers.

Supports two schema formats:

| Format | Source | Mapping |
|--------|--------|---------|
| **GraphQL** | `.graphql` / `.gql` SDL files | Query fields → `[TraxQuery]` trains, Mutation fields → `[TraxMutation]` trains |
| **OpenAPI** | `.json` / `.yaml` / `.yml` specs | GET → `[TraxQuery]` trains, POST/PUT/DELETE/PATCH → `[TraxMutation]` trains |

## Prerequisites

The `trax-hub` template must be installed:

```bash
dotnet new install Trax.Samples
```

## Installation

```bash
dotnet tool install --global Trax.Cli
```

## Usage

```bash
# Generate from a GraphQL schema
trax generate --schema ./schema.graphql --output ./MyProject --name MyProject

# Generate from an OpenAPI spec
trax generate --schema ./openapi.json --output ./MyProject --name MyProject

# Force schema type (auto-detected from extension by default)
trax generate --schema ./spec.yaml --output ./MyProject --name MyProject --type openapi

# Overwrite existing output
trax generate --schema ./schema.graphql --output ./MyProject --name MyProject --force
```

### Options

| Option | Required | Description |
|--------|----------|-------------|
| `--schema` | Yes | Path to schema file |
| `--output` | Yes | Output directory |
| `--name` | Yes | Project name (namespace + csproj) |
| `--type` | No | Force `graphql` or `openapi` |
| `--force` | No | Overwrite existing output directory |

## Generated Output

Given a schema with `createPlayer` and `getPlayer` operations:

```
MyProject/
├── MyProject.Hub/                    # From dotnet new trax-hub
│   ├── MyProject.Hub.csproj          # + ProjectReference to trains library
│   ├── Program.cs                    # Patched: AddMediator scans trains assembly
│   ├── appsettings.json
│   └── Trains/                       # Template sample trains (kept as examples)
│       └── ...
├── MyProject.Trains/                 # Generated from schema
│   ├── MyProject.Trains.csproj       # Class library
│   ├── ManifestNames.cs              # Centralized manifest external IDs
│   ├── Models/
│   │   └── Player.cs
│   └── Trains/
│       └── Players/
│           ├── CreatePlayer/
│           │   ├── ICreatePlayerTrain.cs
│           │   ├── CreatePlayerTrain.cs
│           │   ├── CreatePlayerInput.cs
│           │   └── Junctions/
│           │       └── CreatePlayerJunction.cs
│           └── GetPlayer/
│               ├── IGetPlayerTrain.cs
│               ├── GetPlayerTrain.cs
│               ├── GetPlayerInput.cs
│               └── Junctions/
│                   └── GetPlayerJunction.cs
```

Operations are grouped into folders by noun, so `createPlayer`, `getPlayer`, `deletePlayer` all go under `Players/`.

Each junction contains a `throw new NotImplementedException()` with a TODO comment. For OpenAPI sources, the original HTTP method and path are included as a comment.

`ManifestNames.cs` contains `const string` identifiers for each operation in kebab-case, matching the pattern used in the DistributedWorkers sample for scheduler topology registration.

## After Generating

```bash
cd MyProject/MyProject.Hub
dotnet restore
# Fill in junction implementations in MyProject.Trains/ (search for TODO)
dotnet run
# Open http://localhost:5000/trax/graphql for GraphQL IDE
# Open http://localhost:5000/trax for Dashboard
```

## License

MIT

## Trademark & Brand Notice

Trax is an open-source .NET framework provided by TraxSharp. This project is an independent community effort and is not affiliated with, sponsored by, or endorsed by the Utah Transit Authority, Trax Retail, or any other entity using the "Trax" name in other industries.
