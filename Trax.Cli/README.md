# Trax.Cli

[![NuGet Version](https://img.shields.io/nuget/v/Trax.Cli)](https://www.nuget.org/packages/Trax.Cli/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

CLI tool for [Trax](https://www.nuget.org/packages/Trax.Effect/) — generate Trax API projects from GraphQL or OpenAPI schemas.

## What This Does

Takes an existing API schema and scaffolds a Trax project with two parts: an API project (from the `trax-api` template) and a shared trains library with trains, junctions, input/output records, and models. The trains library follows the same structure as the DistributedWorkers sample — it can be referenced by an API, scheduler, or standalone workers.

Supports two schema formats:

| Format | Source | Mapping |
|--------|--------|---------|
| **GraphQL** | `.graphql` / `.gql` SDL files | Query fields → `[TraxQuery]` trains, Mutation fields → `[TraxMutation]` trains |
| **OpenAPI** | `.json` / `.yaml` / `.yml` specs | GET → `[TraxQuery]` trains, POST/PUT/DELETE/PATCH → `[TraxMutation]` trains |

## Prerequisites

The `trax-api` template must be installed:

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
├── MyProject.Api/                    # From dotnet new trax-api
│   ├── MyProject.Api.csproj          # + ProjectReference to trains library
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

Operations are grouped into folders by noun — `createPlayer`, `getPlayer`, `deletePlayer` all go under `Players/`.

Each junction contains a `throw new NotImplementedException()` with a TODO comment. For OpenAPI sources, the original HTTP method and path are included as a comment.

`ManifestNames.cs` contains `const string` identifiers for each operation in kebab-case, matching the pattern used in the DistributedWorkers sample for scheduler topology registration.

## After Generating

```bash
cd MyProject/MyProject.Api
dotnet restore
# Fill in junction implementations in MyProject.Trains/ (search for TODO)
dotnet run
# Open http://localhost:5002/trax/graphql
```

## Part of Trax

Trax is a layered framework — each package builds on the one below it. Stop at whatever layer solves your problem.

```
Trax.Core              pipelines, junctions, railway error propagation
└→ Trax.Effect         + execution logging, DI, pluggable storage
   └→ Trax.Mediator       + decoupled dispatch via TrainBus
      └→ Trax.Scheduler      + cron schedules, retries, dead-letter queues
         └→ Trax.Api          + GraphQL API layer
            └→ Trax.Dashboard       + Blazor monitoring UI
Trax.Cli               ← you are here (standalone tool)
```

Full documentation: [traxsharp.net/docs](https://traxsharp.net/docs)

## License

MIT

## Trademark & Brand Notice

Trax is an open-source .NET framework provided by TraxSharp. This project is an independent community effort and is not affiliated with, sponsored by, or endorsed by the Utah Transit Authority, Trax Retail, or any other entity using the "Trax" name in other industries.
