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

### Supported schemas

GraphQL SDL, and OpenAPI 2.0 (Swagger) and 3.0 documents in JSON or YAML. OpenAPI 3.1 is not read: an
ASP.NET Core 10 API writes 3.1 by default, so export 3.0 for `trax generate` by setting
`options.OpenApiVersion = OpenApiSpecVersion.OpenApi3_0` in `AddOpenApi`. A file the CLI cannot read (3.1,
a YAML file that is not OpenAPI, a JSON, YAML or SDL syntax error, an unknown `--type` or extension) is
reported in one line naming the file, with the line and column for an SDL error, and the command exits 1.

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

Fields added by `extend type Query`, `extend type Mutation`, or an `extend` of any other object, input or
enum type are merged into the type they extend, so a modular schema concatenated into one file generates
every operation it declares. An `extend` of a type the schema never defines is refused. Any other kind of
definition the generator does not read is named in a warning rather than skipped silently.

Field arguments become properties on the train's input record. A single argument that is one input object
(`createPlayer(input: PlayerInput!)`) contributes that input type's fields directly; a single argument that is
a list of them (`createPlayers(inputs: [PlayerInput!]!)`) stays one `List<PlayerInput>` property. The return
type maps to the output record or a shared model type.

The group a train is filed under is the field's noun: a leading verb (`get`, `add`, `set`, `list`, `update`
and so on) is removed only where it is its own word, so `addPlayer` goes under `Players` and `settings`
under `Settings`.

### OpenAPI / REST

Each endpoint becomes a train. `GET` endpoints become `[TraxQuery]` trains; `POST`, `PUT`, `DELETE`, and `PATCH` endpoints become `[TraxMutation]` trains.

Path parameters, query parameters, and request body fields are merged into a single input record. A body
with properties (inline, through a `$ref`, or across the members of an `allOf`) contributes each property. A
body without them (an array, a map, a primitive, a `oneOf`) becomes one property holding the whole body,
named by the operation's `x-codegen-request-body-name`, the Swagger 2.0 body parameter's name, or `Body`; an
object body that declares nothing adds nothing. The response schema becomes the output type.

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
│   ├── MyProject.Trains.csproj       # Class library, Trax versions pinned to the hub's
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
- **Trains library**: a class library containing all the domain code. Its csproj references each Trax
  package at the exact version the hub pins in its `Directory.Packages.props`, which does not reach the
  sibling folder, so the two projects always resolve the same Trax release. A hub that pins none (an
  outdated `trax-hub` template) is refused; reinstall `Trax.Samples.Templates`. So is a pin that is not a
  NuGet version or version range (`1.2.3`, `1.2.3-beta.1`, `1.*`, `[1.0,2.0)`), since it is written into the
  trains library's csproj, and a hub `Directory.Packages.props` or csproj that cannot be opened is reported as
  `Cannot read <path>: ...`.
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
| Custom scalars (`scalar UUID`) | `string`, with a TODO naming the scalar |
| Interfaces and unions | `object`, with a TODO naming the type |

A type with a field of its own name (`type Status { status: String! }`) is refused, because C# does not let
a member share its type's name.

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
| `$ref` to a component that only names a primitive (`PlayerId: {type: string, format: uuid}`) | that primitive's type (`Guid`) |
| `$ref` to an object component with no properties | `object` (wherever it appears, maps and nested lists included) |

A `null` entry in a nullable string enum (`enum: [active, banned, null]`) is not a member; the property's
nullability carries it. Any other value in a string enum that is not a string is refused.

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

# Print its states and transitions (or --format mermaid|dot for a diagram source).
trax machine show --assembly ./bin/MyApp.dll

# Fail (exit 1) if any committed artifact is stale (the CI gate).
trax machine check --assembly ./bin/MyApp.dll \
  --ir-out ./machines/checkout --twin-out ./web/src/app/checkout --corpus-out ./machines/checkout \
  --engine-src ./vendor/state-machine/src
```

### `trax machine new <name>`

Scaffolds one declarative C# file, `<Name>Machine.cs`: the state and trigger enums, a context record, a
guarded transition, and the differential wiring, ready for `trax machine generate`. The machine derives from
`Machine<TState, TTrigger>` in `Trax.Effect.StateMachine.Persistence`, so the project holding the file needs
that package, at the version the CLI prints (the one it is built against). `generate` then reads the built
assembly, not the source.

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
directory, the twin next to the frontend). Pass at least one `--*-out`. The run is atomic and idempotent: every
artifact is staged first, a node step that exits 0 without writing what it should is refused, and the staged
files are then swapped into place together, so a failure at any point, including an output root that cannot
be written, leaves every committed artifact as it was and removes any output directory the run created. If
putting a previous artifact back fails too, the error does not claim nothing changed: it names each artifact
and the `<file>.<token>.bak` path its previous content was left at, to move back by hand.

The assembly is searched for machines among the types that load. A type that cannot (a controller built on
ASP.NET Core, which the CLI does not carry, or a class deriving from a dependency missing from the build
output) is skipped, so a machine can live in a web project; when no machine is found the error lists why the
other types did not load.

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

### `trax machine show`

Prints a compiled machine's states and transitions, so a machine can be read or reviewed without its C# or
its IR. It loads the machine exactly as `generate` does.

| Option | Required | Description |
|--------|----------|-------------|
| `--assembly` | Yes | Compiled assembly (`.dll`) containing the machine. |
| `--machine` | No | Full type name of the machine. Required only when the assembly has more than one. |
| `--format` | No | `text` (default), `mermaid` (a `stateDiagram-v2` source) or `dot` (Graphviz). |
| `--ascii` | No | Draw the text format with ASCII only, for a terminal without box or arrow glyphs. |

The text format is one block per state, in the IR's order, with each outgoing edge:

```
checkout  v1   initial: Cart   committed: Paid

Cart ◀ initial
  ── Next ─────▶ Review   guard: Add an item before reviewing.

Paid ■ committed
  ── Restart ──▶ Cart     reduce: reset

Review
  ── Back ─────▶ Cart
  ── Pay ──────▶ Paid     guard: A payable order needs items, a positive total, and a receipt.   reduce: set   ⚡ effect ICheckoutCharge (send only)
```

A guard shows its `Because(...)` text, or its rule when it has none; a reducer shows its kind; an edge that
runs an exactly-once effect shows the effect type and that it is send-only. A state no edge from the initial
state reaches is marked `unreachable from the initial state`, and a state with no outgoing edge prints
`(no transitions)`. Colour is used only when output goes to a terminal and `NO_COLOR` is unset. A control
character in a name or guard message (an escape sequence, a carriage return) is printed as `?`, in every
format, so a machine cannot recolour, clear or retitle the terminal that shows it.

### `trax machine migrate`

Reserved for scaffolding a forward migration by diffing the context schema. Migrations are not yet carried in
the IR (a stored snapshot whose version does not match is rejected and the client starts fresh), so the command
prints that notice to stderr and exits 1, which fails a CI step that runs it rather than reporting a migration
that never happened.

## SDK Reference

> [ExportIr](/docs/sdk-reference/statemachine-api/fluent-authoring#exporting-the-ir) | [IR format](/docs/sdk-reference/statemachine-api/ir-format)
