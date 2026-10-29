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
  - Trax.StateMachine/dotnet/src/Trax.Client/GraphQLSubscriptionSocket.cs
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

It references `Trax.StateMachine` and [`Trax.Client`](/docs/sdk-reference/client-api/dotnet-client), and nothing from
`Trax.Api`, so it fits a MAUI or Blazor WebAssembly app. It sends each operation by the persisted id the host's [client manifest](/docs/sdk-reference/statemachine-api/client-manifest)
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

public static class MachinePredictor
{
    public static IMachinePredictor FromIr(ClientManifestMachine machine);
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
| `MachineError` | Why the controller stopped for good: its `Code` (`GONE`, `CLIENT_OUTDATED`, or `UNAUTHENTICATED` or `FORBIDDEN` for a socket the host refused), `Message`, and with `GONE`, `Deleted`: true when the instance was deleted, false when the server answered `NOT_FOUND`, which it answers alike for a missing instance and another caller's. |
| `ClientProblemCodes` | The codes the client reports itself: `Gone`, `TransportError`, `NothingToRetry`, `Disposed` (`GONE`, `TRANSPORT_ERROR`, `NOTHING_TO_RETRY`, `DISPOSED`). Every other code is a [result code](/docs/sdk-reference/statemachine-api/result-codes). |

### Predicting

| Member | What it is |
| --- | --- |
| `MachinePredictor.FromIr(machine)` | The predictor built from the machine's IR as the manifest lists it, with an [`IrMachine`](/docs/sdk-reference/statemachine-api/ir-machine): no reference to the host's assembly, the way every language's client builds its machine. A snapshot the server sends is read trusting the custom state requirements it has no handler for. It holds no handler for any custom rule, so an event with an edge whose guard or reduction is custom, or that enters a state with a custom requirement, is sent with no prediction, and `CanSend` says only whether the state has an edge for it. |
| `MachinePredictor.For(machine)` | The predictor over a compiled machine's engine (`BuiltMachine`, what `MachineBuilder.Build` answers). Its `IrHash` is the SHA-256 of the machine's exported IR; the controller refuses one whose hash is not the manifest's, since the server would answer every request `CLIENT_OUTDATED`. |
| `IMachinePredictor` | What the controller predicts with: `IrHash`, `Advance`, `Rehydrate`, `Serialize`, `CanFire` and `AvailableTriggers`, each the engine's own. |

`FromIr` is the usual choice: the app needs only the manifest. It has no handler for a custom rule, so an event whose
prediction depends on a custom guard, custom state requirement or custom reduction goes with no `clientResult`, and the
view shows the server's answer when it comes rather than a guess. `For` suits an app that already compiles the host's
machine, and custom rules then predict with their C# handlers. Without a predictor the app shows each event once the
server answers it.

### Talking to a host

| Member | What it is |
| --- | --- |
| `IMachineServer` | The seam the controller drives: `StartAsync`, `EventAsync`, `GetAsync` and `Subscribe`. A refusal is an answer; a failed request throws, and the controller reports it as `TRANSPORT_ERROR`. |
| `GraphQLMachineServer` | The `IMachineServer` over a host's typed `machines` root. It POSTs `{ id, variables }` for each operation through `TraxHttpTransport.PostAsync`, keeping only the variables its document declares, and subscribes to `<machine>Changed` by id. `CLIENT_OUTDATED` and `SERVER_OUTDATED` are answers; any other GraphQL error, or a response with no GraphQL answer in it, throws. A stream that ends without saying why is subscribed again after `ResubscribeDelays` (`DefaultResubscribeDelays`: 1, 2, then 5 seconds); a socket the host refused is not. `DiscardAsync` deletes one of the caller's instances, and answers null when it is gone. |
| `GraphQLMachineServerOptions` | `Http`, the `HttpClient` the requests go through (set its authentication on it, as for any other, and turn its automatic redirects off, which would carry the body and a default header to another host: a client on an `HttpClientHandler` or `SocketsHttpHandler` that follows them, `new HttpClient()` among them, is refused when the server is built, and an answer from another host than the endpoint's fails the request; its `Timeout` bounds each request, body included); the GraphQL `Endpoint`; the `Machine` from the manifest, with its documents, `changed` among them; the `Socket`, an `ISubscriptionSocket` from `Trax.Client`; `ResubscribeDelays`; `MaxResponseSize`, the largest body read from an answer (16 MiB); and `AllowInsecureTransport`, without which an `http` endpoint that is not loopback is refused. |
| `ServerAnswer`, `ServerSnapshot`, `ServerProblem` | An answer: the instance `Id` on a start, the `Snapshot` the server stored or the `Problem` it refused with (`Of` and `Refused` build one). A `ServerSnapshot` is the `State`, its `Context` with no null member, the `Revision` (null when the store records none, which the controller takes and cannot order), the incarnation `RowId`, and `DueAt`, which counts only when `DueAtKnown`. |
| `StreamMessage` | What a change stream delivers: `Changed` with the `Snapshot`, `Gone` (with `NotFound` when the server answered `NOT_FOUND`), `Reload`, `ClientOutdated`, or `Refused` with the `Code` (`UNAUTHENTICATED` or `FORBIDDEN`) and `Message` of a socket the host refused. |

The socket is `Trax.Client`'s, in the namespace `Trax.Client`: `GraphQLSubscriptionSocket`, its options, the
`ISubscriptionSocket` and `ISubscriptionSink` seam (`Subscribe(payload, sink)`, and the sink's `Next`, `Error`,
`Complete` and `Started`), `SocketClosedException` and `SubscriptionErrorException`. The
[`Trax.Client` reference](/docs/sdk-reference/client-api/dotnet-client#the-socket) describes them. The server subscribes
with a payload of the `changed` document's persisted `id` and the instance id as its `variables`. A
`SocketClosedException` whose `CloseStatus` is 4401 or 4403, or whose `HttpStatus` is 401 or 403, is the host refusing
the socket: subscribing again would get the same answer, so the stream delivers `Refused` (`UNAUTHENTICATED` for 4401
and 401, `FORBIDDEN` for 4403 and 403, with the exception's message, which names the code) and is not subscribed
again. Every other `Error` the socket raises, and every `Complete`, is a stream that ended without saying why, which it
subscribes to again after `ResubscribeDelays`.

## Behavior

The controller behaves as the TypeScript `ServerMachineController` does, and the client conformance kit
(`Trax.StateMachine/conformance/client/`) holds both to the same transcripts.

- **Ordering.** Every snapshot, pushed or answered, carries its revision; one at or below the revision held is dropped.
  An answer with no revision is applied and not ordered.
  A push of another incarnation (the instance was deleted and created again) makes the controller read the instance
  again rather than compare revisions across incarnations.
- **Effects.** An event bound to the machine's effect is sent with no prediction, and nothing queued after it is
  predicted until it answers. So is an event with an edge that rests on a custom rule the predictor has no handler
  for.
- **Timers** are the server's: `DueAt` is shown and never fired. An answer that does not know it keeps the one held
  while the state stays; entering a timed state without one reads the instance once for it.
- **Stopping.** A gone instance, `CLIENT_OUTDATED` and a socket the host refused (`UNAUTHENTICATED` or `FORBIDDEN`)
  stop the controller for good, and every queued send settles `Stopped`. A snapshot the predictor cannot read stops it
  as `CLIENT_OUTDATED` too.
- **Transport.** An endpoint must be `https` (`wss` for the socket) unless its host is loopback (`localhost`,
  `127.0.0.0/8`, `::1`). Any other `http` or `ws` endpoint throws when the server or socket is built, since whatever
  authenticates the client would travel in cleartext; `AllowInsecureTransport` accepts it on a network you trust.
  An answer larger than `MaxResponseSize` fails its request as a transport failure, and a socket message larger than
  `MaxMessageSize` fails the connection, which is subscribed again; neither is read into memory first.
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
// No automatic redirects: a redirect would carry any default header but Authorization to whatever host it names.
using var http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false });
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
        Predictor = MachinePredictor.FromIr(turnstile),
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
