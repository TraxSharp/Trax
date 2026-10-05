---
layout: default
title: CLI
description: "The trax CLI, which scaffolds a hub project and a trains library from a GraphQL SDL file or OpenAPI spec: installation, options, mapping and output."
parent: Reference
nav_order: 5
---

# Trax CLI

The Trax CLI generates Trax projects from existing API schemas. Point it at a GraphQL SDL file or an OpenAPI spec and it scaffolds a hub project (via `dotnet new trax-hub`: the GraphQL API, the scheduler and the dashboard in one process) alongside a shared trains library with trains, junctions, input/output records, and wiring, following the same structure as the DistributedWorkers sample.

## Prerequisites

- The `trax-hub` template must be installed. It ships in the `Trax.Samples.Templates` package
  (see [Project Templates](/docs/reference/templates)):

```bash
dotnet new install Trax.Samples.Templates
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
| `--force` | No | Replace the output directory if it already exists, once generation has succeeded |

`generate` builds the project in a hidden directory beside `--output` and moves it into place only
when every step has succeeded, so a failed run (most often a missing `trax-hub` template) leaves an
existing directory exactly as it was. `--force` is refused for the current directory, any of its
parents, and a directory with a git repository (`.git`) in it or anywhere below it, such as a
folder of side-by-side repositories; generate into a new directory instead.

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

### Names and descriptions

Every name in the schema becomes C#: types, properties, enums and their values, operations,
the group each operation is filed under (its first OpenAPI tag, or the noun of a GraphQL field)
and the `--name` project name. Names become identifiers, namespaces and file paths, so after
the PascalCase conversion below each one must match `[A-Za-z_][A-Za-z0-9_]*`; `--name` may be
several of those joined by dots. A schema with any name that does not is refused before
anything is written (and before `--force` deletes anything), and the command exits 1 with every
offending name listed. Rename them in the schema and run it again.

The conversion already handles separators: `first-name`, `first_name` and `first.name` all
become `FirstName`. What it refuses is a name that is still not an identifier afterwards, such
as `2fa`, `application/json` as an enum value, a non-ASCII letter, or OData's `@odata.type`.
It also refuses two names in one type or one enum that the conversion turns into the same one,
such as `first-name` and `firstName` on one schema, or `in-progress` and `inProgress` in one
enum: the generated record would declare the member twice. Neither is renamed or dropped.
An OpenAPI operation's parameters and body properties share one input record, so the same rule
covers them: a path parameter `update_value` and a query parameter `updateValue` are refused. A
path parameter the body repeats under the same name (`id` in the path and in the body) is one
value and appears once.

The same goes for separate definitions that end up with one name. Two OpenAPI component schemas
whose last dotted segment is the same (`Billing.Dto` and `Shipping.Dto` both become `Dto`), a
type and an enum of one name, two operations (`user_count` and `userCount`), or two groups are
refused, and the message names each definition involved. Names that differ only in case
(`PlayerStats` and `Playerstats`) are refused too, because each becomes a file or folder and
those are the same path on macOS and Windows. Without the refusal one definition would silently
take the other's fields or overwrite its files.

Names the generator makes up itself are numbered instead of refused, since the schema never
chose them: an inline object or enum is named after its property, and a second `status` enum
with different values becomes `Status2` rather than reusing the first. An inline name never
takes the name of a component schema.

Descriptions and OpenAPI paths are copied as text, never refused. Each one is collapsed onto a
single line, and escaped for where it lands: XML markup is escaped in `///` comments, and
backslashes and quotes are escaped in the `Description = "..."` string of the train attribute.

`trax machine new` holds its arguments to the same rule: the machine name must make a
PascalCase identifier and `--namespace` must be a dotted one.

## Schema-to-Train Mapping

### GraphQL

Each field on the `Query` type becomes a `[TraxQuery]` train. Each field on the `Mutation` type becomes a `[TraxMutation]` train. Subscription fields are skipped.

Field arguments become properties on the train's input record. The return type maps to the output record or a shared model type.

### OpenAPI / REST

Each endpoint becomes a train. `GET` endpoints become `[TraxQuery]` trains; `POST`, `PUT`, `DELETE`, and `PATCH` endpoints become `[TraxMutation]` trains.

Path parameters, query parameters, and request body fields are merged into a single input record. The response schema becomes the output type.

## Generated Project Structure

The CLI produces two projects: a hub project (from the `trax-hub` template) and a shared trains library (generated from the schema). This follows the same pattern as the DistributedWorkers sample.

Given a schema with a `createPlayer` mutation and `getPlayer` query:

```
MyProject/
├── MyProject.Hub/                    # From dotnet new trax-hub
│   ├── MyProject.Hub.csproj          # + ProjectReference to trains library
│   ├── Directory.Packages.props      # The hub's package versions
│   ├── Program.cs                    # Patched: AddMediator scans trains assembly
│   ├── README.md
│   ├── appsettings.json
│   ├── Auth/, Data/                  # Template demo key and application DbContext
│   ├── Trains/                       # Template sample trains (HelloWorld, Lookup)
│   │   └── ...
│   └── tests/MyProject.Hub.Tests/    # Template test project
├── MyProject.Trains/                 # Generated from schema
│   ├── MyProject.Trains.csproj       # Class library (not web SDK)
│   ├── ManifestNames.cs              # Centralized manifest external IDs
│   ├── GraphQLNamespaces.cs          # One constant per operation group
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

- **Hub project**: the `trax-hub` template (GraphQL API, scheduler and dashboard in one process), with its `Program.cs` patched to scan the trains library assembly and a `ProjectReference` to the trains library.
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

### Model names and framework types

A model may share its name with a .NET type: a schema with `Task`, `File` or `Exception` types
generates code that compiles. The trains, interfaces and junctions refer to framework types and
to the models by their fully qualified `global::` names, not through a `using` directive for the
models namespace, so neither can shadow the other.

Five names are the exception, because the mappings above write them for the framework type:
`Guid`, `DateTime`, `DateOnly`, `Uri`, and `Unit` (what an operation returning nothing produces).
A schema type or enum with one of those names is refused with the other names the generator cannot
emit; rename it in the schema.

## After Generating

1. `cd` into the hub project directory (`MyProject/MyProject.Hub`)
2. Run `dotnet restore`
3. Search for `TODO` in the junction files under `MyProject.Trains/` and implement your business logic
4. Run `dotnet run`. The hub uses the in-memory data provider, so no database is needed; switch
   it to Postgres as described in [Project Templates](/docs/reference/templates#switching-to-postgres)
   when you need data to outlive the process
5. Open `http://localhost:5400/trax/graphql` for the GraphQL IDE, and `http://localhost:5400/trax`
   for the dashboard (Development only). Every GraphQL operation needs the header
   `X-Api-Key: demo-key-do-not-use-in-production`

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

The machine's id (what its `Id(...)` sets) names every artifact, so it must be kebab-case: lowercase
letters and digits in words joined by single hyphens, starting with a letter (`checkout`,
`write-to-congress`), the form `trax machine new` produces. `generate` and `check` refuse any other id
before writing anything.

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
prints that notice to stderr and exits 1, which fails a CI step that runs it rather than reporting a migration
that never happened.

## SDK Reference

> [ExportIr](/docs/sdk-reference/statemachine-api/fluent-authoring#exporting-the-ir) | [IR format](/docs/sdk-reference/statemachine-api/ir-format)
