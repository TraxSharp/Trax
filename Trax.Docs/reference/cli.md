---
layout: default
title: CLI
parent: Reference
nav_order: 5
---

# Trax CLI

The Trax CLI generates Trax API projects from existing API schemas. Point it at a GraphQL SDL file or an OpenAPI spec and it scaffolds an API project (via `dotnet new trax-api`) alongside a shared trains library with trains, junctions, input/output records, and wiring, following the same structure as the DistributedWorkers sample.

## Prerequisites

- The `trax-api` template must be installed:

```bash
dotnet new install Trax.Samples
```

## Installation

Install as a global .NET tool:

```bash
dotnet tool install --global Trax.Cli
```

## Usage

```bash
trax generate --schema <path> --output <dir> --name <project-name> [--type graphql|openapi] [--force]
```

### Options

| Option | Required | Description |
|--------|----------|-------------|
| `--schema` | Yes | Path to the schema file (`.graphql`, `.gql`, `.json`, `.yaml`, `.yml`) |
| `--output` | Yes | Output directory for the generated project |
| `--name` | Yes | Project name (used for namespace and `.csproj`) |
| `--type` | No | Force schema type: `graphql` or `openapi`. Auto-detected from file extension if omitted. |
| `--force` | No | Overwrite the output directory if it already exists |

### Examples

```bash
# Generate from a GraphQL schema
trax generate --schema ./schema.graphql --output ./MyProject --name MyProject

# Generate from an OpenAPI spec
trax generate --schema ./openapi.json --output ./MyProject --name MyProject

# Force schema type detection
trax generate --schema ./spec.yaml --output ./MyProject --name MyProject --type openapi

# Overwrite existing output
trax generate --schema ./schema.graphql --output ./MyProject --name MyProject --force
```

## Schema-to-Train Mapping

### GraphQL

Each field on the `Query` type becomes a `[TraxQuery]` train. Each field on the `Mutation` type becomes a `[TraxMutation]` train. Subscription fields are skipped.

Field arguments become properties on the train's input record. The return type maps to the output record or a shared model type.

### OpenAPI / REST

Each endpoint becomes a train. `GET` endpoints become `[TraxQuery]` trains; `POST`, `PUT`, `DELETE`, and `PATCH` endpoints become `[TraxMutation]` trains.

Path parameters, query parameters, and request body fields are merged into a single input record. The response schema becomes the output type.

## Generated Project Structure

The CLI produces two projects: an API project (from the `trax-api` template) and a shared trains library (generated from the schema). This follows the same pattern as the DistributedWorkers sample.

Given a schema with a `createPlayer` mutation and `getPlayer` query:

```
MyProject/
├── MyProject.Api/                    # From dotnet new trax-api
│   ├── MyProject.Api.csproj          # + ProjectReference to trains library
│   ├── Program.cs                    # Patched: AddMediator scans trains assembly
│   ├── appsettings.json
│   └── Trains/                       # Template sample trains (HelloWorld, Lookup)
│       └── ...
├── MyProject.Trains/                 # Generated from schema
│   ├── MyProject.Trains.csproj       # Class library (not web SDK)
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

### What gets generated

- **API project**: a fully wired Trax API from the `trax-api` template, with its `Program.cs` patched to scan the trains library assembly and a `ProjectReference` to the trains library.
- **Trains library**: a class library containing all the domain code:
  - **ManifestNames.cs**: centralized `const string` identifiers for each operation (kebab-case), matching the pattern used in the DistributedWorkers sample.
  - **Trains** are grouped into folders by noun (e.g., `createPlayer` and `getPlayer` both go under `Players/`).
  - **Shared types** referenced by multiple operations are placed in `Models/`.
  - **Enums** are also placed in `Models/`.
  - **Junctions** contain a `throw new NotImplementedException()` with a TODO comment. This is where you add your business logic.
  - For OpenAPI endpoints, the junction includes the original HTTP method and path as a comment.

### Why two projects?

This structure separates infrastructure from domain logic. The trains library can be referenced by multiple projects (an API, a scheduler, standalone workers) without duplicating train definitions. This is the same pattern demonstrated in the DistributedWorkers sample with `Trax.Samples.EnergyHub`.

## Type Mapping

### GraphQL to C#

| GraphQL | C# |
|---------|----|
| `String` | `string` |
| `ID` | `string` |
| `Int` | `int` |
| `Float` | `double` |
| `Boolean` | `bool` |
| `DateTime` | `DateTime` |
| `Long`, `BigInt` | `long` |
| `Decimal` | `decimal` |
| `[T]` | `List<T>` |
| `T!` | `required T` |
| `T` (nullable) | `T?` |
| Custom scalars | `string` (with TODO) |

### OpenAPI to C#

| OpenAPI | C# |
|---------|----|
| `string` | `string` |
| `string` + `date-time` | `DateTime` |
| `string` + `date` | `DateOnly` |
| `string` + `uuid` | `Guid` |
| `string` + `uri` | `Uri` |
| `string` + `binary` | `byte[]` |
| `integer` | `int` |
| `integer` + `int64` | `long` |
| `number` | `double` |
| `number` + `float` | `float` |
| `boolean` | `bool` |
| `array` | `List<T>` |
| `object` + `additionalProperties` | `Dictionary<string, T>` |
| `$ref` | Named C# record |
| `enum` (string) | C# `enum` |

## After Generating

1. `cd` into the API project directory (`MyProject/MyProject.Api`)
2. Run `dotnet restore`
3. Search for `TODO` in the junction files under `MyProject.Trains/` and implement your business logic
4. Start PostgreSQL (`docker compose up -d` or similar)
5. Update the connection string in `appsettings.json` if needed
6. Run `dotnet run` to start the API
7. Open `http://localhost:5002/trax/graphql` in a browser for the GraphQL playground

## State machines (`trax machine`)

The `machine` command group scaffolds a [Tier-1 state machine](/docs/statemachine) and regenerates its
artifacts from the C# source: the [IR](/docs/sdk-reference/statemachine-api/ir-format), the TypeScript twin,
and the differential corpus. It replaces regenerating those by hand (or through a chain of update-flagged
tests), and it is the one command you run after every machine edit. See
[the codegen pipeline](/docs/statemachine/codegen-pipeline) for how the pieces fit together.

The IR is exported in-process from the compiled machine; the twin and corpus are produced by the engine's own
generators, so twin/corpus generation needs `node` (>= 22) on `PATH` and the engine's `src` directory.

```bash
# Scaffold a new machine as one declarative C# file.
trax machine new checkout --output ./Machines --namespace MyApp.Machines --with-effect

# Export the IR, twin, and corpus (each to its own output root).
trax machine generate --assembly ./bin/MyApp.dll \
  --ir-out ./machines/checkout --twin-out ./web/src/app/checkout --corpus-out ./machines/checkout \
  --engine-src ./vendor/state-machine/src

# Fail (exit 1) if any committed artifact is stale (the CI gate).
trax machine check --assembly ./bin/MyApp.dll \
  --ir-out ./machines/checkout --twin-out ./web/src/app/checkout --corpus-out ./machines/checkout \
  --engine-src ./vendor/state-machine/src
```

### `trax machine new <name>`

Scaffolds one declarative C# file, `<Name>Machine.cs`: the state and trigger enums, a context record, a
guarded transition, and the differential wiring, ready for `trax machine generate`.

| Option | Required | Description |
|--------|----------|-------------|
| `<name>` | Yes | Machine name as a kebab-case id (`checkout`, `write-to-congress`). The type prefix is the PascalCase form. |
| `--output` | No | Directory to write `<Name>Machine.cs` (default: current directory). |
| `--namespace` | No | Namespace for the generated file (default: `Machines`). |
| `--with-effect` | No | Include an exactly-once `ISnapshotEffect` stub and mark the terminal state committed. |
| `--force` | No | Overwrite the file if it already exists. |

### `trax machine generate`

Exports the IR from a compiled machine, then generates the twin and/or corpus. Each artifact has its own
output root, because a consumer typically splits them across trees (the IR and corpus in a shared machines
directory, the twin next to the frontend). Pass at least one `--*-out`; the run is atomic (a failed step
leaves every output root untouched) and idempotent.

| Option | Required | Description |
|--------|----------|-------------|
| `--assembly` | Yes | Compiled assembly (`.dll`) containing the machine. |
| `--machine` | No | Full type name of the machine. Required only when the assembly has more than one. |
| `--ir-out` | No | Directory to write `<id>.ir.json`. |
| `--twin-out` | No | Directory to write `<id>.contexts.g.ts` and `<id>.machine.g.ts`. |
| `--corpus-out` | No | Directory to write `differential.json`. |
| `--engine-src` | For twin/corpus | The TypeScript engine's `src` directory. |
| `--import-style` | No | Twin engine imports: `relative` (default, for a machine inside the engine repo) or `specifier` (one collapsed import from `--specifier`, for a consumer that vendors the engine behind a path alias). |
| `--specifier` | No | Module specifier used with `--import-style specifier` (default `@trax/state-machine`). |
| `--tools-dir` | No | The engine's `tools/` directory (default: a sibling of `--engine-src`). |
| `--node` | No | Path to the `node` executable (default: `node`). |

### `trax machine check`

Takes the same options as `generate`. It regenerates to a temp location and diffs against what is committed,
printing `ok` / `DRIFT` / `MISSING` per artifact and exiting non-zero on any drift. Because it is the same code
path as `generate`, the two cannot disagree. Wire it into CI to fail a build whose artifacts are stale.

### `trax machine migrate`

Reserved for scaffolding a forward migration by diffing the context schema. Migrations are not yet carried in
the IR (a stored snapshot whose version does not match is rejected and the client starts fresh), so the command
currently prints that notice; it exists so the surface is complete.

## SDK Reference

> [ExportIr](/docs/sdk-reference/statemachine-api/fluent-authoring#exporting-the-ir) | [IR format](/docs/sdk-reference/statemachine-api/ir-format)
