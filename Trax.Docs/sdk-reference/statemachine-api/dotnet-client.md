---
title: .NET client
description: "Reference for Trax.StateMachine.Client: driving a machine a host serves from Blazor, MAUI, WPF or another .NET service, by persisted id."
type: reference
area: machines
parent: State Machine API
grand_parent: SDK Reference
nav_order: 17
package: Trax.StateMachine.Client
namespace: Trax.StateMachine.Client
source:
  - Trax.StateMachine/dotnet/src/Trax.StateMachine.Client/ServerMachineController.cs
  - Trax.StateMachine/dotnet/src/Trax.StateMachine.Client/IMachineServer.cs
  - Trax.StateMachine/dotnet/src/Trax.StateMachine.Client/GraphQLMachineServer.cs
  - Trax.StateMachine/dotnet/src/Trax.StateMachine.Client/GraphQLSubscriptionSocket.cs
  - Trax.StateMachine/dotnet/src/Trax.StateMachine.Client/MachinePredictor.cs
---

# .NET client

`Trax.StateMachine.Client` drives one instance of a machine a host serves on its typed
[`machines` root](/docs/sdk-reference/graphql-api/machines), from any .NET frontend or service: Blazor WebAssembly,
.NET MAUI, Avalonia, WPF, or a service driving a machine on another host. It is the C# counterpart of the TypeScript
package's `ServerMachineController` and `createMachineServer`, and it passes the same client conformance transcripts.

```bash
dotnet add package Trax.StateMachine.Client
```

It references `Trax.StateMachine` and nothing from `Trax.Api`, so it fits a MAUI or Blazor WebAssembly app. It sends
each operation by the persisted id the host's [client manifest](/docs/sdk-reference/statemachine-api/client-manifest)
lists, never a document of its own, so a host that requires persisted operations accepts it.

## Signatures

```csharp fragment
public sealed class ServerMachineController : IDisposable
{
    public ServerMachineController(ServerMachineOptions options);
    public static readonly IReadOnlyList<TimeSpan> DefaultRetryDelays;

    public MachineView View { get; }
    public event EventHandler? Changed;

    public Task StartAsync();
    public Task<SendOutcome> SendAsync(string trigger, JsonNode? input = null);
    public Task<SendOutcome> RetryAsync();
    public bool CanSend(string trigger, JsonNode? input = null);
    public IReadOnlyList<string> AvailableTriggers();
    public void Dispose();
}

public sealed partial class GraphQLMachineServer : IMachineServer
{
    public GraphQLMachineServer(GraphQLMachineServerOptions options);
    public static readonly IReadOnlyList<TimeSpan> DefaultResubscribeDelays;
    public Task<ServerProblem?> DiscardAsync(string id, string requestId, CancellationToken cancellationToken = default);
}

public sealed class GraphQLSubscriptionSocket : ISubscriptionSocket, IAsyncDisposable
{
    public GraphQLSubscriptionSocket(GraphQLSubscriptionSocketOptions options);
}

public static class MachinePredictor
{
    public static IMachinePredictor For<TState, TTrigger>(BuiltMachine<TState, TTrigger> machine)
        where TState : struct, Enum
        where TTrigger : struct, Enum;
}
```

## Members

### Driving an instance

| Member | What it is |
| --- | --- |
| `ServerMachineController` | Drives one instance. The server applies every transition; the controller predicts with the `Predictor`, if it has one, and shows the server's answer when it comes. Every operation is total: a refusal or a failed request is a `SendOutcome`, never an exception. |
| `StartAsync()` | Opens the instance: subscribes to its changes, then reads it, so no write falls between the two. Without an `Id` it starts one under `StartRequestId` and subscribes to the id the server minted. A second call returns the same task. |
| `SendAsync(trigger, input)` | Sends one event under a fresh request id, after every earlier one has settled. While it is pending, `View.Snapshot` is the prediction, and the prediction's canonical wire goes with the event as its `clientResult`. |
| `RetryAsync()` | Resends what last failed under the same request id, so the server replays it rather than applying it twice: the start while there is no instance, else the last event that failed, unless a later one was confirmed since. With nothing to resend it answers `Failed` with `NOTHING_TO_RETRY`. |
| `CanSend(trigger, input)`, `AvailableTriggers()` | Whether the predictor allows a trigger from what is shown, and the triggers with an edge out of the shown state. Without a predictor, `CanSend` is true for any trigger with such an edge: the server decides. Both are false or empty before the first snapshot and once stopped. |
| `View`, `Changed` | The current `MachineView`, a new object on every change, and the event raised after each change. |
| `Dispose()` | Closes the stream and drops every late answer. Pending sends settle as `Failed` with `DISPOSED`. |
| `DefaultRetryDelays` | 250 ms, 500 ms, 1 s, 2 s and 4 s: the delays between retries of `SERVER_OUTDATED` and of a failed read. |
| `ServerMachineOptions` | What the controller is built with: the `Machine` as the manifest lists it, the `Server`, an optional `Predictor`, the `Id` of an instance to open or the `StartInput` and `StartRequestId` to start one, `NewRequestId` and `RetryDelays`. |
| `MachineView` | `Status` (a `MachineStatus`: `Loading`, `Ready`, `Stopped`), the instance `Id`, the `Snapshot` shown, the `Confirmed` one, `RowId`, `Revision`, `DueAt`, `Pending`, `Problem` and `Error`. |
| `SendOutcome` | What one send came to: `Confirmed` with the server's `Snapshot`, `Refused` or `Failed` with the `Problem`, or `Stopped` with the `Error`. |
| `MachineProblem`, `MachineProblemKind` | The last refusal or transport failure: its `Kind` (`Rejected` or `Transport`), its `Code` and its `Message`. |
| `MachineError` | Why the controller stopped for good: its `Code` (`GONE` or `CLIENT_OUTDATED`), `Message`, and with `GONE`, `Deleted`: true when the instance was deleted, false when the server answered `NOT_FOUND`, which it answers alike for a missing instance and another caller's. |
| `ClientProblemCodes` | The codes the client reports itself: `Gone`, `TransportError`, `NothingToRetry`, `Disposed` (`GONE`, `TRANSPORT_ERROR`, `NOTHING_TO_RETRY`, `DISPOSED`). Every other code is a [result code](/docs/sdk-reference/statemachine-api/result-codes). |

### Predicting

| Member | What it is |
| --- | --- |
| `MachinePredictor.For(machine)` | The predictor over a compiled machine's engine (`BuiltMachine`, what `MachineBuilder.Build` answers). Its `IrHash` is the SHA-256 of the machine's exported IR; the controller refuses one whose hash is not the manifest's, since the server would answer every request `CLIENT_OUTDATED`. |
| `IMachinePredictor` | What the controller predicts with: `IrHash`, `Advance`, `Rehydrate`, `Serialize`, `CanFire` and `AvailableTriggers`, each the engine's own. |

A client predicts only with the C# machine the host serves, compiled into it: the C# engine does not build a machine
from an IR. A frontend that does not share the machine's assembly leaves `Predictor` null and shows each event once the
server answers it.

### Talking to a host

| Member | What it is |
| --- | --- |
| `IMachineServer` | The seam the controller drives: `StartAsync`, `EventAsync`, `GetAsync` and `Subscribe`. A refusal is an answer; a failed request throws, and the controller reports it as `TRANSPORT_ERROR`. |
| `GraphQLMachineServer` | The `IMachineServer` over a host's typed `machines` root. It POSTs `{ id, variables }` for each operation, keeping only the variables its document declares, and subscribes to `<machine>Changed` by id. `CLIENT_OUTDATED` and `SERVER_OUTDATED` are answers; any other GraphQL error or a status that is not success throws. A stream that ends without saying why is subscribed again after `ResubscribeDelays` (`DefaultResubscribeDelays`: 1, 2, then 5 seconds). `DiscardAsync` deletes one of the caller's instances, and answers null when it is gone. |
| `GraphQLMachineServerOptions` | `Http`, the `HttpClient` the requests go through (set its authentication on it, as for any other); the GraphQL `Endpoint`; the `Machine` from the manifest, with its documents, `changed` among them; the `Socket`; and `ResubscribeDelays`. |
| `GraphQLSubscriptionSocket` | A graphql-transport-ws socket that subscribes by persisted id. It connects on the first subscription and closes after the last. It does not reconnect by itself: a closed socket fails every open subscription with its `Error`, and the server subscribes again. |
| `GraphQLSubscriptionSocketOptions` | The socket `Endpoint`; `ConnectionParams`, the `connection_init` payload, read on every connection so a rotating token stays fresh; `ConfigureSocket`, to set headers or a proxy on each `ClientWebSocket`; and `ConnectTimeout`. |
| `ISubscriptionSocket`, `ISubscriptionSink` | The socket seam: `Subscribe(documentId, variables, sink)`, and the sink's `Next`, `Error` and `Complete`. |
| `SubscriptionErrorException` | The error a sink gets when the server ends a subscription with GraphQL errors, which it carries as `Errors`. |
| `ServerAnswer`, `ServerSnapshot`, `ServerProblem` | An answer: the instance `Id` on a start, the `Snapshot` the server stored or the `Problem` it refused with (`Of` and `Refused` build one). A `ServerSnapshot` is the `State`, its `Context` with no null member, the `Revision`, the incarnation `RowId`, and `DueAt`, which counts only when `DueAtKnown`. |
| `StreamMessage` | What a change stream delivers: `Changed` with the `Snapshot`, `Gone` (with `NotFound` when the server answered `NOT_FOUND`), `Reload`, or `ClientOutdated`. |

## Behavior

The controller behaves as the TypeScript `ServerMachineController` does, and the client conformance kit
(`Trax.StateMachine/conformance/client/`) holds both to the same transcripts.

- **Ordering.** Every snapshot, pushed or answered, carries its revision; one at or below the revision held is dropped.
  A push of another incarnation (the instance was deleted and created again) makes the controller read the instance
  again rather than compare revisions across incarnations.
- **Effects.** An event bound to the machine's effect is sent with no prediction, and nothing queued after it is
  predicted until it answers.
- **Timers** are the server's: `DueAt` is shown and never fired. An answer that does not know it keeps the one held
  while the state stays; entering a timed state without one reads the instance once for it.
- **Stopping.** A gone instance and `CLIENT_OUTDATED` stop the controller for good, and every queued send settles
  `Stopped`. A snapshot the predictor cannot read stops it as `CLIENT_OUTDATED` too.
- **Threads.** The controller works on one exclusive scheduler, so it handles one thing at a time whichever thread calls
  it. `Changed` is raised there: read `View` and marshal to the UI thread in the handler, and do not block on the
  controller from it.

## Example

```csharp compile
var manifest = ClientManifest.Read(await File.ReadAllTextAsync("manifest.json"));
var turnstile = manifest.Machines.Single(m => m.Id == "turnstile");

await using var socket = new GraphQLSubscriptionSocket(
    new GraphQLSubscriptionSocketOptions { Endpoint = new Uri("wss://shop.example/trax/graphql") }
);
using var http = new HttpClient();
var server = new GraphQLMachineServer(
    new GraphQLMachineServerOptions
    {
        Http = http,
        Endpoint = new Uri("https://shop.example/trax/graphql"),
        Machine = turnstile,
        Socket = socket,
    }
);

using var controller = new ServerMachineController(
    new ServerMachineOptions
    {
        Machine = turnstile,
        Server = server,
        Predictor = MachinePredictor.For(Turnstile.Build()),
    }
);
await controller.StartAsync();

var outcome = await controller.SendAsync("Coin", new JsonObject { ["coin"] = "quarter" });
// SendOutcome.Confirmed when the server stored it; Refused, Failed or Stopped otherwise, never a throw
```

## See also

- [TypeScript package](/docs/sdk-reference/statemachine-api/typescript-package), the same client for a web page.
- [Client manifest](/docs/sdk-reference/statemachine-api/client-manifest), where the documents come from.
- [The machines root](/docs/sdk-reference/graphql-api/machines), the operations the client sends.
- [Result codes](/docs/sdk-reference/statemachine-api/result-codes), the codes every answer carries.
