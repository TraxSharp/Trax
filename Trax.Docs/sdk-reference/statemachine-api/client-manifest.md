---
title: Client manifest
description: "Reference for the client manifest a host exports for trax client: RunTraxAsync, --trax-export-client, ClientManifest and its startup checks."
type: reference
area: machines
parent: State Machine API
grand_parent: SDK Reference
nav_order: 16
package: Trax.Effect.StateMachine
namespace: Trax.Effect.StateMachine
source:
  - Trax.Effect/src/Trax.Effect.StateMachine/ClientManifest.cs
  - Trax.Effect/src/Trax.Effect/Configuration/ClientExport/TraxClientExport.cs
  - Trax.Effect/src/Trax.Effect/Configuration/ClientExport/ITraxStartupCheck.cs
  - Trax.Api/src/Trax.Api.GraphQL.PersistedOperations/Extensions/TraxClientManifestExtensions.cs
---

# Client manifest

A host exports one JSON file that `trax client` reads to generate a frontend's client: every persisted document a
client may send, by id, each exposed machine's IR, and the GraphQL types those documents reach. The host writes it
with the code that registers its documents, so every id in it is one the host serves. The CLI reads only the file, so
a frontend's build needs no running server, no database and no credential, and Trax.Cli never references Trax.Api.

## Members

Three namespaces: `ClientManifest` and its records are in `Trax.Effect.StateMachine`; `TraxClientExport` and
`ITraxStartupCheck` in `Trax.Effect.Configuration.ClientExport`; `RunTraxAsync` in
`Trax.Api.GraphQL.PersistedOperations.Extensions`.

| Member | Description |
| --- | --- |
| `app.RunTraxAsync(string[]? args = null)` | Runs the host as `app.RunAsync()` does, unless the process was started with `--trax-export-client <path>`: then it runs the host's startup checks, writes the manifest to the path and returns without serving. `args` defaults to the process's command line. In `Trax.Api.GraphQL.PersistedOperations.Extensions`. |
| `TraxClientExport.Argument` | `--trax-export-client`. It takes the path next (`--trax-export-client out/client.json`) or after `=`. |
| `TraxClientExport.Requested`, `TraxClientExport.Path` | Whether, and where to, the process was started to export, read from its command line when first used. `UsePostgres` and `UseSqlite` skip their migration when it is set; a host skips its own seeding the same way. The argument without a path throws an `ArgumentException` that says so where it is read. |
| `TraxClientExport.PathIn(IReadOnlyList<string> args)` | The path `args` name after the argument, or null. The argument without a path throws. |
| `ITraxStartupCheck` | Marks a hosted service that only checks configuration and the host's own types at startup and refuses a host that is wrong. An export runs every one of these and no other hosted service. Trax's own startup validators carry it, except the two that read the database or call a remote schema. |
| `ClientManifest` | The file's content: `Format`, `Machines`, `Trains`, `Progress` and `Types`. `Progress` holds the [`runProgress`](/docs/sdk-reference/graphql-api/run-progress) query's and subscription's documents, and is empty when no train lets its starter follow it. `ClientManifest.Write(manifest)` gives its text, `ClientManifest.Read(text)` reads it. `ClientManifest.CurrentFormat` is the format this version writes and reads. |
| `ClientManifestMachine` | One exposed machine: `Id`, `IrHash`, `Ir` and its `Documents`. `Requires` is null: each event's own rule applies. `IrText()` gives the IR as canonical JSON, the bytes `IrHash` is the SHA-256 of. |
| `ClientManifestTrain` | One exposed train: its `Field`, `Root`, `Namespace`, `Answer` (the field's type as SDL writes it), `Requires` and `Documents`, and `Progress`, whether it carries `[TraxRunProgress]`. |
| `ClientManifestDocument` | One persisted document: the `Id` a client sends, its `Operation` and its `Document` text. |
| `ClientManifestRequirement` | What a caller needs, as the train's attributes declare it: `Anonymous` (`[TraxAllowAnonymous]`), `RoleSets` (one per attribute naming roles; the caller holds a role of every set) and `Policies` (each of them). The server enforces it either way. |

## Behavior

- **Nothing runs but the checks.** The export resolves the schema and runs every `ITraxStartupCheck` before it
  writes, so a configuration production refuses fails the export too. No other hosted service starts: nothing
  listens, sweeps, registers documents or opens a database. A host that seeds data at startup checks
  `TraxClientExport.Requested` first.
- **Started to export, never serves.** A host started with `--trax-export-client` whose provider skipped its
  migration refuses to start if it serves anyway, because it calls `app.Run()` rather than `RunTraxAsync`: its
  database was not migrated. The refusal names `RunTraxAsync`.
- **Only what a client sends.** The manifest lists the documents of exposed machines and trains, never one under
  `operations`. `Types` holds the SDL of the types those documents reach, and of the fields a train's document left out
  because they lead back to a type already in its answer, so a [client document](/docs/api/fetch-only-what-a-component-needs)
  may select them; nothing else. The root operation types
  and every type a namespace field reaches (`dispatch`, `discover`, `machines` and the namespaces under them) are
  left out, since each lists every sibling operation, so the rest of the schema stays on the server. A train's
  answer type is in its `Answer` instead.
- **The same bytes.** `Write` writes compact JSON and a final newline, the same bytes for the same content, so a
  committed manifest can be compared in CI.
- **Shared output schemas once.** When two or more outcomes, in any of the machines, carry the same output schema
  (invoking states whose trains answer with one type), `Write` puts it once under a top-level `$defs` and each
  outcome's `output` is `{"$ref": "#/$defs/<name>"}`. `Read` puts it back, so each machine's `Ir`, and the
  `IrHash` its documents carry, is the one the host exported. The generated twin likewise writes such a schema once,
  as a const each outcome names. This is format 2; a format 1 file is exported again.
- **One format.** `Read` refuses a file in any format other than `CurrentFormat`, naming both, and anything that is
  not a manifest. A newer file needs a newer tool; an older one is exported again.
- **Export the production configuration.** A machine's IR, and so every document id, can depend on configuration (a
  timer's duration, for one). Export with the configuration the host serves in production.

## Example

```csharp fragment
using Trax.Api.GraphQL.PersistedOperations.Extensions; // RunTraxAsync
using Trax.Effect.Configuration.ClientExport; // TraxClientExport

var app = builder.Build();

if (!TraxClientExport.Requested)
    await Seed(app.Services);

app.UseTraxGraphQL();
await app.RunTraxAsync(args);
```

```bash
ASPNETCORE_ENVIRONMENT=Production dotnet run --project Api -- --trax-export-client ../web/src/trax/manifest.json
```

## See also

- [The machines root](/docs/sdk-reference/graphql-api/machines#persisted-documents), the documents the manifest lists.
- [TypeScript package](/docs/sdk-reference/statemachine-api/typescript-package), what the generated client calls.
