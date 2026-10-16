---
title: Trax CLI
description: "The trax CLI: generate scaffolds a hub and a trains library from GraphQL SDL or OpenAPI, and machine scaffolds and exports state machines."
type: reference
area: tooling
nav_order: 1
source:
  - Trax.Cli/src/Trax.Cli/Commands/GenerateCommand.cs
  - Trax.Cli/src/Trax.Cli/Generator
  - Trax.Cli/src/Trax.Cli/Schema
  - Trax.Cli/src/Trax.Cli/Commands/MachineCommand.cs
  - Trax.Cli/src/Trax.Cli/Machines
---

# Trax CLI

The Trax CLI generates Trax projects from existing API schemas. Point it at a GraphQL SDL file or an OpenAPI spec and it scaffolds a hub project (via `dotnet new trax-hub`: the GraphQL API, the scheduler and the dashboard in one process) alongside a shared trains library with trains, junctions, input/output records, and wiring, following the same structure as the DistributedWorkers sample.

## Prerequisites

- The `trax-hub` template must be installed. It ships in the `Trax.Samples.Templates` package
  (see [Project Templates](/docs/reference/templates)):

```bash
dotnet new install Trax.Samples.Templates
```

- `node` 22 or later on `PATH`, for `trax machine generate` and `trax client` (and `--check`), which generate twins
  with it. A CI image that runs them needs it too.

## Installation

Install as a global .NET tool:

```bash
dotnet tool install --global Trax.Cli
```

Or, so CI runs the version the repository pins, as a local tool. Create the manifest once at the repository root and
commit the `dotnet-tools.json` it writes (`.config/dotnet-tools.json` or `./dotnet-tools.json`, depending on the SDK):

```bash
dotnet new tool-manifest
dotnet tool install Trax.Cli
```

A fresh checkout, such as a CI container, restores it before running it:

```bash
dotnet tool restore
dotnet tool run trax client --manifest web/src/trax/manifest.json --out web/src/trax --check
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

[`trax machine new`](#trax-machine-new-name) holds its arguments to the same rule: the machine name must make a
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
| `Date` | `DateOnly` |
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

## Output

On success `generate` prints the two project folders and these next steps, and exits 0:

1. `cd` into the hub project directory (`MyProject/MyProject.Hub`).
2. `dotnet restore`.
3. Fill in the junctions under `MyProject.Trains/`: each throws `NotImplementedException` beside a
   `TODO`.
4. `dotnet run`.

The hub keeps the template's in-memory data provider, so it runs with no database
([Switch a template to Postgres](/docs/reference/switch-a-template-to-postgres) moves it), and the
template's URLs: `http://localhost:5400/trax/graphql` for the GraphQL IDE and
`http://localhost:5400/trax` for the dashboard (Development only). Every GraphQL operation needs the
header `X-Api-Key: demo-key-do-not-use-in-production`.

## State machines (`trax machine`)

The `machine` command group scaffolds a [Tier-1 state machine](/docs/statemachine) and regenerates its
artifacts from the C# source: the [IR](/docs/sdk-reference/statemachine-api/ir-format), the TypeScript twin,
and the differential corpus. It replaces regenerating those by hand (or through a chain of update-flagged
tests), and it is the one command you run after every machine edit. See
[the codegen pipeline](/docs/statemachine/codegen-pipeline) for how the pieces fit together.

The IR is exported in-process from the compiled machine; the twin and corpus are produced by the engine's own
generators, which ship inside the `trax` tool with the engine compiled in. Twin and corpus generation needs
only `node` (>= 22) on `PATH`: no engine source and no npm install. The twin imports
[`@traxsharp/state-machine`](/docs/sdk-reference/statemachine-api/typescript-package), which the frontend
installs from npm. The installed package need not match the tool's version, only the IR format the twin records;
see [the codegen pipeline](/docs/statemachine/codegen-pipeline#generation-and-drift).

```bash
# Scaffold a new machine as one declarative C# file.
trax machine new checkout --output ./Machines --namespace MyApp.Machines --with-effect

# Export the IR, twin, and corpus (each to its own output root).
trax machine generate --assembly ./bin/MyApp.dll \
  --ir-out ./machines/checkout --twin-out ./web/src/app/checkout --corpus-out ./machines/checkout

# Print its states and transitions (or --format mermaid|dot for a diagram source).
trax machine show --assembly ./bin/MyApp.dll

# Fail (exit 1) if any committed artifact is stale (the CI gate).
trax machine check --assembly ./bin/MyApp.dll \
  --ir-out ./machines/checkout --twin-out ./web/src/app/checkout --corpus-out ./machines/checkout
```

### `trax machine new <name>`

Scaffolds one declarative C# file, `<Name>Machine.cs`: the state and trigger enums, a context record, a
guarded transition, and the differential wiring, ready for `trax machine generate`. The machine derives from
`Machine<TState, TTrigger>` in `Trax.Effect.StateMachine.Persistence`, so the project holding the file needs
that package, at the version the CLI prints (the one it is built against). `generate` then reads the built
assembly, not the source.

| Option | Required | Description |
|--------|----------|-------------|
| `<name>` | Yes | Machine name as a kebab-case id (`checkout`, `ship-to-store`). The type prefix is the PascalCase form. |
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
`ship-to-store`), the form `trax machine new` produces. `generate` and `check` refuse any other id
before writing anything.

| Option | Required | Description |
|--------|----------|-------------|
| `--assembly` | Yes | Compiled assembly (`.dll`) containing the machine. |
| `--machine` | No | Full type name of the machine. Required only when the assembly has more than one. |
| `--ir-out` | No | Directory to write `<id>.ir.json`. |
| `--twin-out` | No | Directory to write `<id>.contexts.g.ts` and `<id>.machine.g.ts`. |
| `--corpus-out` | No | Directory to write `differential.json`. |
| `--engine-src` | No | Generate the twin and corpus from this engine checkout's `src` directory, with the `tools/` beside it, instead of the engine bundled into the tool. For working on the engine itself; it needs `npm ci` in that checkout. |
| `--import-style` | No | Twin engine imports: `specifier` (one collapsed import from `--specifier`, for a frontend that installs `@traxsharp/state-machine` from npm; the default) or `relative` (paths into the engine's `src`, for a machine inside the engine checkout; the default with `--engine-src`, and refused without it, since those paths resolve only inside the checkout). |
| `--specifier` | No | Module specifier used with `--import-style specifier` (default `@traxsharp/state-machine`): an npm package name, optionally with a subpath (`@acme/engine/machines`). Anything else is refused, and so is `--specifier` with relative imports, where it would be ignored. |
| `--tools-dir` | No | The directory holding `generate-twin.mjs` and `generate-corpus.mjs` (default: the `tools/` beside `--engine-src`, else the ones bundled into the tool). It needs `--engine-src`, which those generators read the engine from. |
| `--node` | No | The `node` executable: an absolute path, or a name looked up on `PATH` (default: `node`). Only absolute `PATH` entries are searched, never the current directory, so a `node` file at the root of the checkout you run in is not run. A relative path such as `./node` is refused. On Windows only a real executable (`node.exe`) is run: a `node.cmd` or `node.bat` shim, which some version managers put on `PATH`, runs through `cmd.exe`, which parses its arguments a second time, so it is skipped in favour of a `node.exe` later on `PATH`, or refused by name when it is the only one, and a `--node` naming one is refused. |

#### Changes to `generate`'s defaults

Earlier versions of the tool required `--engine-src` to generate a twin and imported the engine by relative path,
and `--import-style specifier` defaulted to `state-machine` in the `@trax` scope, which Trax does not own on npm.
Now:

- without `--engine-src`, the twin imports the installed `@traxsharp/state-machine` (`--import-style specifier`);
  with it, the default is still `relative`;
- the default `--specifier` is `@traxsharp/state-machine`;
- a specifier twin's first lines name the import it was generated with and the options that change it, and its IR
  records the IR format the installed engine checks on load.

To generate as before, pass `--import-style relative --engine-src <engine/src>`, or `--specifier <module>` with the
name your frontend imports the engine by. See
[Regenerate twins after the trax machine generate defaults changed](/docs/migration-guides/trax-machine-generate-defaults).

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
runs an exactly-once effect shows the effect type and that it is send-only. A state that invokes a train lists
the train's outcome edges under their triggers (`Dealing.done`, `Dealing.failed`, `Dealing.cancelled`), in the
order they are tried, and they count toward reachability like any other edge. A state no edge from the initial
state reaches is marked `unreachable from the initial state`, and a state with no outgoing edge prints
`(no transitions)`. Colour is used only when output goes to a terminal and `NO_COLOR` is unset. A control
character in a name or guard message (an escape sequence, a carriage return) is printed as `?`, in every
format, so a machine cannot recolour, clear or retitle the terminal that shows it.

### `trax machine migrate`

Reserved for scaffolding a forward migration by diffing the context schema. Migrations are not yet carried in
the IR (a stored snapshot whose version does not match is rejected and the client starts fresh), so the command
prints that notice to stderr and exits 1, which fails a CI step that runs it rather than reporting a migration
that never happened.

## Frontend clients (`trax client`)

`trax client` generates a frontend's typed client from the [client manifest](/docs/sdk-reference/statemachine-api/client-manifest)
a host exports, and reads nothing else: no running host, no database, no credential.

```bash
dotnet run --project Api -- --trax-export-client web/src/trax/manifest.json   # on the host, production configuration
trax client --target react --manifest web/src/trax/manifest.json --out web/src/trax
dotnet tool restore && dotnet tool run trax client --target react --manifest web/src/trax/manifest.json --out web/src/trax --check   # in CI
```

| Option | What it does |
| --- | --- |
| `--target` | The framework. `react`, the default, is the only one today. |
| `--manifest` | The manifest the host wrote with `--trax-export-client`. Required. |
| `--out` | The directory the client is written to, inside the frontend's source. Required. A file `trax client` wrote under it that the manifest no longer generates is deleted, and a folder that leaves empty. A client module is its own when its first line is its header. A twin is when it is `<machine>/<machine>.machine.g.ts` or `.contexts.g.ts` with the twin generator's header for that machine, and the previous run served the machine: the `manifest.json` it left in `--out` names it, or `<machine>/<machine>.client.g.ts` is still there. With neither, a twin is kept, so one `trax machine generate` wrote is never deleted. Any other file is left alone, a linked folder is not entered, and a file that is not a regular one (a FIFO, a device) is not read. A link at a path it writes, or at a folder on the way there, is refused, and nothing is written. |
| `--check` | Writes nothing; exits 1 when the client in `--out` is not what the manifest generates, naming each file. A checkout that writes CRLF is not a difference. |
| `--specifier` | The module the client imports the runtime from (default `@traxsharp/state-machine`). |
| `--tools-dir`, `--node` | As for `trax machine generate`: the twins are generated by the engine's own generator, under node 22 or later. |
| `--documents` | A glob of `.graphql` documents the frontend wrote (`src/**/*.graphql`), relative to the current directory; repeatable. `*` matches within a folder, `**/` any folders, `?` one character. A glob that matches nothing is an error, and the `schema.graphql` it writes under `--out` is never read. See [Fetch only what a component needs](/docs/api/fetch-only-what-a-component-needs). |

It writes, under `--out`:

| File | What it holds |
| --- | --- |
| `format.g.ts` | The client format the code was generated for, checked against the installed runtime (`requireClientFormat`) before any other generated module runs. |
| `client.g.ts` | `configureTrax({ url, socketUrl, headers?, connectionParams?, user? })`, which the app calls at startup and again when another user signs in, the shared socket and `TraxOperationError`. `user` is the signed-in user's id, which the remembered machines are kept under. Calling it again closes the socket, a machine server made before refuses to send (`CONNECTION_CHANGED`: its requests reject with it, and a controller over it stops with it rather than subscribing again), and every mounted machine hook renders again onto a server over the new connection and the new user, showing nothing of the instance before until the new one answers. Headers are read per request. `socketUrl` is an absolute `ws://` or `wss://` URL: build it from `location` (`` `${location.protocol === "https:" ? "wss" : "ws"}://${location.host}/trax/graphql` ``) rather than passing a path. A credential such as an API key goes in both `headers`, for HTTP requests, and `connectionParams`, for the socket, since a browser's WebSocket carries no custom headers. Nothing about the host is generated into it. |
| `<machine>/<machine>.machine.g.ts`, `<machine>.contexts.g.ts` | The machine's twin. |
| `<machine>/<machine>.client.g.ts` | The machine's documents by id, `<machine>Server()`, `use<Machine>(options)` over `useServerMachine`, which keeps a start in flight under `trax-start:<machine id>.<user>`, and `useRemembered<Machine>(options)` over `useRememberedMachine`, which keeps the instance of the `user` given to `configureTrax` across visits under `trax.<machine id>.<user>` (`rememberKey` replaces the `trax.<machine id>`). |
| `types.g.ts` | With `--documents`, or when a train can be followed: the enums and inputs the trains and query models take, as TypeScript, but the filter and sort inputs, which a document writes in its text. An answer's object types are not declared here: each is typed where it is answered, so no type promises a field its document does not fetch. To name a part of an answer, index the result type (`PaymentsListChargesResult["charges"][number]`). |
| `progress.g.ts` | When a train carries [`[TraxRunProgress]`](/docs/sdk-reference/graphql-api/run-progress): the `RunProgress` and `RunProgressEvent` types its two documents answer, `readRunProgress(runId)`, `followRunProgress(runId, onEvent, onEnd?)`, whose `onEnd` gets a `TraxOperationError` `CONNECTION_CHANGED` when `configureTrax` closes the socket before the run ends, and `useRunProgress(runId)`, which reads your run once, follows it until it ends, reads it again after a lost event, and follows a failed run into the retry the scheduler queues (`runId` is the run shown, `earlier` the runs before it). A read that answers after a newer event arrived does not show its state over the event's, but the steps it holds that no event reported are shown. The run is read again once its stream opens and once more after it ends, so a hook mounted mid-run also shows a step the host sent before the stream opened and wrote after the first read. After `configureTrax` the run is read and followed again over the new connection, from an empty view: nothing the hook showed before (its state, steps, `ended` or `problem`) is shown on the new connection. When `configureTrax` names another `user`, the run id given before is the previous user's: the hook follows nothing and shows the empty view until a run id is given again. A token refresh for the same `user` follows the run again. |
| `documents/<operation>.g.ts` | With `--documents`: `<operation>(variables, options?)`, the camel-cased operation name, which sends the document's id (`trax_client_<name>_<hash of its text>`) and answers `<Operation>Result`, the fields it selects, typed from the manifest's types, a union or interface as one object per possible type told apart by `__typename`. A query model answers its connection (`nodes`, `pageInfo`, `totalCount` as selected) or its list. A query of several trains and query models answers each under its key, inside its namespace. Its doc comment names what each requires of a caller. This is the only function a train gets: the host generates no document for a train. |
| `fragments.g.ts` | With `--documents`: each named fragment as a type of its name. Its fields are also written out where it is spread, so a component typed with the fragment takes any answer that has them. |
| `trax.documents.json` | With `--documents`: every operation as the host registers it, `{ "format": 1, "documents": [{ "name", "source", "text" }] }` in name order. `text` is the operation printed and every fragment it spreads, so reformatting a file or editing a comment keeps its id. The host names it in [`ClientDocuments(path)`](/docs/sdk-reference/persisted-operations/persisted-operations-builder). |
| `schema.graphql`, `graphql.config.yml` | With `--documents`: the manifest's types and the roots and namespaces that hold each train and query model, without directives, and a config naming them and the `--documents` globs, for the GraphQL editor extension. |
| `index.g.ts`, `manifest.json` | Everything above re-exported, and the manifest it was generated from. |

The generated client is committed, like a twin, so a frontend's build needs no .NET. CI exports the manifest from the
host and runs `--check`, which also catches a HotChocolate change that moved a document's id. Every name from the
manifest is checked before it is written: a machine id or an operation name that is a word TypeScript reserves, two
that generate one name (`map-2d` and `map2d`), two documents whose files differ only in case, a train field that is not
a GraphQL name, and a name the client's runtime exports (`configureTrax`) or a machine's client module declares
(`server`, `socket`) are refused; a string is
written as a literal and a description cannot close its comment. Each machine's IR must name the machine, its states,
triggers and outcomes must be identifiers and no state or trigger may be named after an `Object.prototype` member
(`__proto__`, `constructor`), its `context`, `invariants` and `inputs` must be keyed by its own states and triggers,
no object in it may hold a `__proto__` key, and it must hash to the `irHash` its documents carry, or nothing is generated.
With `--documents`, each document is checked as the host checks it at startup: one named query or mutation per file
(a file may hold only fragments), its root `discover` or `dispatch`, under it only the manifest's trains and their
namespaces, a mutation exactly one train, no directive; every field, argument, variable and fragment as the
manifest's types allow, no variable unused, no fragment unused or spreading itself, and no key naming two different
selections. Every refusal is reported at once, each with its file, line and column. An operation or fragment name
that another part of the client already uses is refused like any other name. A malformed manifest is refused with a sentence
saying where. The app installs `graphql-ws` beside `@traxsharp/state-machine` (`npm install @traxsharp/state-machine
graphql-ws`), and the generated code typechecks under the `tsconfig` a Vite React template writes
(`erasableSyntaxOnly`, `verbatimModuleSyntax`, `noUnusedLocals`).

## SDK Reference

> [ExportIr](/docs/sdk-reference/statemachine-api/machine#exporting-the-ir) | [IR format](/docs/sdk-reference/statemachine-api/ir-format)
