---
title: .NET client
description: "Reference for Trax.Client: TraxClient, TraxDocument, TraxHttpTransport and GraphQLSubscriptionSocket, which send a host's persisted GraphQL documents from .NET."
type: reference
area: api
parent: Client API
grand_parent: SDK Reference
nav_order: 1
package: Trax.Client
namespace: Trax.Client
source:
  - Trax.StateMachine/dotnet/src/Trax.Client/TraxClient.cs
  - Trax.StateMachine/dotnet/src/Trax.Client/TraxDocument.cs
  - Trax.StateMachine/dotnet/src/Trax.Client/TraxHttpTransport.cs
  - Trax.StateMachine/dotnet/src/Trax.Client/TraxAnswer.cs
  - Trax.StateMachine/dotnet/src/Trax.Client/Optional.cs
  - Trax.StateMachine/dotnet/src/Trax.Client/TraxJson.cs
  - Trax.StateMachine/dotnet/src/Trax.Client/GraphQLSubscriptionSocket.cs
---

# .NET client

`Trax.Client` sends a Trax host's GraphQL documents from .NET: a query or mutation as an HTTP POST, a subscription over
one graphql-transport-ws socket. A document goes by the persisted id the host registered it under, or as text to a host
that runs it unregistered. Every answer is a value: data, the host's errors, or a transport failure. A refusal never
throws.

```bash
dotnet add package Trax.Client
```

It references the base class library and nothing else, so a terminal tool, a MAUI app or a Blazor WebAssembly app can
take it. It is marked trimmable and AOT-compatible, and builds with the trim and AOT analyzers reporting nothing; see
[Trimming and native AOT](#trimming-and-native-aot) for what a trimmed app passes it.
The typed documents it sends are generated: `trax client --operations --lang csharp` writes a
`TraxDocument<TVariables, TResult>` per document with its variables and result records, in the namespace `--namespace`
names. [`Trax.StateMachine.Client`](/docs/sdk-reference/statemachine-api/dotnet-client) posts and subscribes through
this package too.

## Signatures

```csharp fragment
public sealed class TraxClient
{
    public TraxClient(ITraxTransport transport, TraxClientOptions? options = null);
    public static readonly IReadOnlyList<TimeSpan> DefaultResubscribeDelays;
    public ITraxTransport Transport { get; }

    public Task<TraxAnswer<TResult>> SendAsync<TVariables, TResult>(
        TraxDocument<TVariables, TResult> document, TVariables variables, CancellationToken cancellationToken = default);
    public IDisposable Subscribe<TVariables, TResult>(
        TraxDocument<TVariables, TResult> document, TVariables variables, Action<TraxStreamEvent<TResult>> onEvent);
}

public abstract record TraxDocument(string Name, TraxOperationKind Kind, string? Id, string Text);
public sealed record TraxDocument<TVariables, TResult>(string Name, TraxOperationKind Kind, string? Id, string Text)
    : TraxDocument(Name, Kind, Id, Text);

public interface ITraxTransport
{
    Task<TraxAnswer> ExecuteAsync(TraxRequest request, CancellationToken cancellationToken = default);
    IDisposable Subscribe(TraxRequest request, ISubscriptionSink sink);
}

public sealed class TraxHttpTransport : ITraxTransport
{
    public TraxHttpTransport(TraxHttpTransportOptions options);
    public JsonObject Payload(TraxRequest request);
}

public sealed class GraphQLSubscriptionSocket : ISubscriptionSocket, IAsyncDisposable
{
    public GraphQLSubscriptionSocket(GraphQLSubscriptionSocketOptions options);
    public IDisposable Subscribe(JsonObject payload, ISubscriptionSink sink);
}

public readonly record struct Optional<T>
{
    public Optional(T value);
    public bool HasValue { get; }
    public T Value { get; }
}
```

## Members

### Sending

| Member | What it is |
| --- | --- |
| `TraxClient` | Sends documents through an `ITraxTransport`, writing variables and reading data with `TraxJson.Options` unless `TraxClientOptions.Json` says otherwise. It never talks to the network itself. Built in an app that turned reflection-based serialization off, it throws `ArgumentException` unless `TraxClientOptions.Json` names a resolver. |
| `SendAsync(document, variables, cancellationToken)` | Sends a query or mutation and reads the answer into a `TraxAnswer<TResult>`. It throws only when `cancellationToken` is cancelled, or for a subscription document, which `Subscribe` follows. Data that does not fit `TResult`, or that the options cannot read at all, is a `Failure`, not a throw. |
| `Subscribe(document, variables, onEvent)` | Follows a subscription until the returned handle is disposed, and calls `onEvent` with each `TraxStreamEvent<TResult>`, one call at a time. Throws `ArgumentException` for a document that is not a subscription. An `onEvent` that throws ends its stream: the last call it gets is `Ended`, whose `Failure` names the exception. Other streams go on. |
| `Transport` | The transport the client sends through. |
| `DefaultResubscribeDelays` | 1, 2, then 5 seconds: the delays before subscribing again after the socket dropped a stream. |
| `TraxClientOptions` | `Json`, the serializer options (`TraxJson.Options` when null), which the client reads through a copy that leaves every unset `Optional<T>` out, whatever their resolver; `ResubscribeDelays`, whose last delay repeats for as long as the stream is followed (`DefaultResubscribeDelays` when null or empty); `Time`, the `TimeProvider` the delays run on (the system's when null). |

### Documents

| Member | What it is |
| --- | --- |
| `TraxDocument` | One document: its operation's `Name`, sent as `operationName`; its `Kind`; the persisted `Id`, or null for a document a host runs as text and never registers; and its `Text`, exactly as the id was computed from it. |
| `TraxDocument<TVariables, TResult>` | A document whose variables are a `TVariables` and whose data is read into a `TResult`. Generated code declares one per document. |
| `TraxOperationKind` | `Query`, `Mutation` or `Subscription`. |
| `TraxNoVariables` | The variables of a document that declares none, sent as `{}`. `TraxNoVariables.Instance` is its one value. |
| `TraxRequest` | A `Document` and its `Variables` as a `JsonObject`: what a transport is handed. |
| `Optional<T>` | A variable or input field that may be left out, which GraphQL tells apart from null. `default` is left out, and the host uses the argument's default or leaves the value as it is; any value converts to one that is sent, null included, which clears it. `HasValue` says whether it is sent and `Value` holds it. |
| `TraxJson.Options` | The web defaults (camelCase, case-insensitive reads), with every `Optional<T>` property that holds no value left out of the JSON. It reads types by reflection. A `TraxClient` leaves an unset `Optional<T>` out with any options given in `TraxClientOptions.Json` too, a source-generated context included; only JSON written outside the client, with options it never saw, carries an unset one as `default(T)`. |
| `OptionalJsonConverterFactory` | The converter `Optional<T>` names in its `[JsonConverter]`, public so a source-generated context in another assembly can create it. Nothing calls it directly. |

### Answers

| Member | What it is |
| --- | --- |
| `TraxAnswer<T>` | A document's answer: `Data` read into the result, the `Errors` the host answered with, or the `Failure` that kept an answer from arriving. `Succeeded` is true when data arrived with no error. `Code` is `TRANSPORT_ERROR` for a failure, else the first error's code. `Message` is the failure's message or every error's, one per line. |
| `TraxAnswer` | An answer as it arrived, what an `ITraxTransport` returns: `Data` as JSON, `Errors` and `Failure`. `TraxAnswer.Of(json)` reads an answer's JSON and is null when it holds neither `data` nor `errors`; `TraxAnswer.Failed(message, status)` is one that never arrived. |
| `TraxGraphQLError` | One error: its `Message`, its `Code` (`extensions.code`, such as `CLIENT_OUTDATED` or `AUTH_NOT_AUTHORIZED`), its `Path` and its `Extensions` as sent. |
| `TraxTransportFailure` | A request that got no GraphQL answer: its `Message`, and the HTTP `Status` when a response arrived. `TraxTransportFailure.Code` is `TRANSPORT_ERROR`, the code the state-machine clients report a transport failure under. |
| `TraxStreamEvent<T>` | What a followed stream delivers: `Next` with one frame's `Data`; `Interrupted` with the `Reason` the socket dropped it; `Resumed` once it is subscribed again, before its first `Next`; `Ended` with the `Errors` or the `Failure` it ended on, both empty only when the host completed it. |

### Transports

| Member | What it is |
| --- | --- |
| `ITraxTransport` | How a request reaches a host: `ExecuteAsync` for a query or mutation, which answers a refusal or an unreachable host as a `TraxAnswer` and throws only on cancellation, and `Subscribe` for a stream. An app can give `TraxClient` its own: a recording, a fake, a wrapper that counts what is in flight. |
| `TraxHttpTransport` | The transport over HTTP POST and a socket. `Payload(request)` is the body or subscribe payload it sends: `id` or `query`, with `operationName` and `variables`. `Subscribe` sends it over the `Socket`, or fails the sink at once with a `SocketUnavailableException` when there is none. |
| `ExecuteAsync(request, cancellationToken)` | POSTs the payload and reads the answer. A body with `errors` is an answer whatever its status, since a host answers a coded refusal with the status it chose, and a success with `data` is one too. Everything else is a `Failure`: a status that is not success with no errors (whatever data came with it), a host that could not be reached, a body that breaks off, a body larger than `MaxResponseSize` (refused as it arrives, never read whole), and a request, body included, that outlasts the `HttpClient`'s `Timeout`. It throws only when `cancellationToken` is cancelled. |
| `TraxHttpTransportOptions` | `Http`, the `HttpClient` the requests go through (set its authentication on it, a default header or a delegating handler; its `Timeout` bounds each request, body included); the GraphQL `Endpoint`; the `Socket` subscriptions go over; `ById`, which says whether a document goes by its id (by default every document with an id does; a host that registered only some of them, or none, names which); `MaxResponseSize`, the largest body read from an answer (`DefaultMaxResponseSize`, 16 MiB); and `AllowInsecureTransport`. Without it the transport refuses, when it is built, an `http` endpoint whose host is not this machine, since whatever authenticates the requests would cross the network in the clear. |

### The socket

| Member | What it is |
| --- | --- |
| `GraphQLSubscriptionSocket` | A graphql-transport-ws socket. It connects when the first subscription opens and closes when the last one ends. Each subscribe message carries the payload it is given. It does not reconnect by itself: a closed or broken connection (a close, a dropped network, a ping left unanswered, a message that is not graphql-transport-ws) fails every open subscription, with a `SocketClosedException` when it closed, and whoever subscribed decides when to subscribe again (`TraxClient` does, after its resubscribe delays). A failed connection is disposed. A sink that throws ends only its own subscription: the socket completes it with the host and hands the exception to that sink's `Error`. |
| `GraphQLSubscriptionSocketOptions` | The socket `Endpoint`; `ConnectionParams`, the `connection_init` payload, read on every connection so a rotating token stays fresh; `ConfigureSocket`, to set headers, a proxy or client certificates on each `ClientWebSocket`, run after the keep-alive is set; `ConnectTimeout` (10 seconds), how long a connection may take to be acknowledged; `KeepAliveInterval` (15 seconds), how often the socket pings the host, and `KeepAliveTimeout` (15 seconds), how long a ping may go unanswered before the connection counts as broken, which is how a host that dropped off the network without closing is found (neither is set in a browser); `MaxMessageSize` (16 MiB), the largest message read, past which the connection fails rather than reading on; and `AllowInsecureTransport`, without which a `ws` endpoint whose host is not this machine is refused. |
| `ISubscriptionSocket` | The socket seam: `Subscribe(payload, sink)`, where `payload` is the subscribe message's payload (`id` or `query`, with `operationName` and `variables`). The returned handle ends the subscription. |
| `ISubscriptionSink` | What a subscription delivers to: `Next` with each GraphQL result, `Error` when the server refused it, the connection closed or broke, or there is no socket, `Complete` when the server ended it, and `Started` once the subscribe message went out over an acknowledged connection (nothing by default). `GraphQLSubscriptionSocket` calls `Started` before the first `Next`, and never calls one subscription's sink from two threads at once. |
| `SocketClosedException` | The `Error` a sink gets when the socket closed, before it was acknowledged or while subscribed, or the host refused to open it. `CloseStatus` is the close code the server sent, if any; `HttpStatus` the status it refused the upgrade with, if it did. `Refused` is true for a 4400 to 4499 close (4401 unauthorized, 4403 forbidden) and for an upgrade answered 401 or 403, the host refusing the connection itself: subscribing again gets the same answer. |
| `SocketUnavailableException` | The `Error` a sink gets when there is no socket to follow the subscription over: the transport has none, or the socket was disposed. |
| `SubscriptionErrorException` | The `Error` a sink gets when the server ends a subscription with GraphQL errors, which it carries as `Errors` (empty when the server sent none). |

## Behavior

- **Totality.** `SendAsync` and `ExecuteAsync` answer every refusal and every transport failure as a value: an
  unreachable host, a broken or oversized body, an answer that does not fit the result. A timeout the caller did not
  ask for is a `Failure`; a cancellation the caller asked for throws.
- **By id or by text.** A document with an id goes by its id unless `ById` says otherwise, and its text is then not
  sent at all. A host that requires persisted operations accepts only those. Either way the request carries
  `operationName`.
- **Streams.** A `Subscribe` stream ends for good (`Ended`) when the host completes it (no errors, no failure), answers a
  frame with errors or refuses the subscription (`Errors`, or a `Failure` when the refusal carried none), or with a
  `Failure` when it closes the socket with a 44xx code, answers the upgrade 401 or 403, sends a frame that does not fit
  the result, when the transport has no socket or the socket was disposed, or when `onEvent` throws. Anything else that
  drops the stream, a close, a broken connection, an unanswered ping or a malformed message, is `Interrupted`, said
  once, and the stream is subscribed again after the next of `ResubscribeDelays`, starting over at the first once a
  subscription starts. `Resumed` follows, before the new subscription's first `Next`; frames sent meanwhile are lost,
  so read again what the stream may have missed. Disposing the handle ends the stream and cancels a pending
  resubscribe.
- **Optional fields.** A generated variables or input record types a field the host may leave as it is as an
  `Optional<T>`, so `MaxActiveJobs = null` sends `"maxActiveJobs": null` and leaving it unset sends nothing, with
  `TraxJson.Options` or any options in `TraxClientOptions.Json`. Read back, a member that is absent is unset and one
  that is null is a null that was sent.

## Trimming and native AOT

`TraxJson.Options` reads types by reflection, which a trimmed or native AOT app turns off by default, and is annotated
so. Such an app passes `TraxClientOptions.Json` with a source-generated `JsonSerializerContext` as its resolver; the
client adds what leaves unset `Optional<T>` fields out. The context lists the generated records and every `T` an
`Optional<T>` wraps, since it cannot see through `Optional<T>`'s converter. Without one, and with reflection off, the
`TraxClient` constructor throws `ArgumentException` rather than failing on the first request.

```csharp fragment
[JsonSerializable(typeof(DisableManifestVariables))]
[JsonSerializable(typeof(DisableManifestResult))]
[JsonSerializable(typeof(string))] // each T an Optional<T> wraps
internal sealed partial class ShopJson : JsonSerializerContext;

var client = new TraxClient(
    transport,
    new TraxClientOptions { Json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = ShopJson.Default } }
);
```

## Example

```csharp compile
using System.Text.Json.Nodes;
using Trax.Client;

using var http = new HttpClient();
http.DefaultRequestHeaders.Add("X-Api-Key", configuration["Trax:ApiKey"]);

await using var socket = new GraphQLSubscriptionSocket(
    new GraphQLSubscriptionSocketOptions
    {
        Endpoint = new Uri("wss://shop.example/trax/graphql"),
        ConnectionParams = _ =>
            ValueTask.FromResult<JsonObject?>(new JsonObject { ["apiKey"] = configuration["Trax:ApiKey"] }),
    }
);
var client = new TraxClient(
    new TraxHttpTransport(
        new TraxHttpTransportOptions
        {
            Http = http,
            Endpoint = new Uri("https://shop.example/trax/graphql"),
            Socket = socket,
        }
    )
);

// DisableManifest and OnTrainStateChanged are generated documents.
var answer = await client.SendAsync(Documents.DisableManifest, new DisableManifestVariables("nightly-sync"));
if (!answer.Succeeded)
    Console.WriteLine($"{answer.Code}: {answer.Message}"); // a refusal or a transport failure, never a throw

using var feed = client.Subscribe(
    Documents.OnTrainStateChanged,
    TraxNoVariables.Instance,
    streamEvent =>
    {
        switch (streamEvent)
        {
            case TraxStreamEvent<OnTrainStateChangedResult>.Next next:
                Console.WriteLine($"{next.Data.OnTrainStateChanged.TrainName}: {next.Data.OnTrainStateChanged.TrainState}");
                break;
            case TraxStreamEvent<OnTrainStateChangedResult>.Resumed:
                Console.WriteLine("Reconnected: read the runs again.");
                break;
        }
    }
);
```

## See also

- [Client API](/docs/sdk-reference/client-api), the package's index.
- [Persisted Operations](/docs/api/persisted-operations), how a host registers the documents it accepts by id.
- [.NET machine client](/docs/sdk-reference/statemachine-api/dotnet-client), which drives a served machine through this
  package.
- [trax client](/docs/reference/cli#frontend-clients-trax-client), which generates the typed documents.
