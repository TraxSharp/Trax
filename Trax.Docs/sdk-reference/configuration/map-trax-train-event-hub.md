---
layout: default
title: MapTraxTrainEventHub
description: Reference for MapTraxTrainEventHub, which maps the Trax SignalR hub behind a required authorization posture, with connection lifetime, send timeout and clients.
parent: Configuration
grand_parent: SDK Reference
nav_order: 14
---

# MapTraxTrainEventHub

Maps the Trax SignalR hub at a URL path. Clients connect here to receive train lifecycle events pushed by the [`UseSignalRHub`](/docs/sdk-reference/configuration/use-signalr-hub) sink. Every connected client receives every train's events, so the hub is mapped with an authorization posture: the host says who may connect, or the host does not start.

## Signature

```csharp
public static HubEndpointConventionBuilder MapTraxTrainEventHub(
    this IEndpointRouteBuilder endpoints,
    Action<TraxTrainEventHubOptions> configure
)

public static HubEndpointConventionBuilder MapTraxTrainEventHub(
    this IEndpointRouteBuilder endpoints,
    string path,
    Action<TraxTrainEventHubOptions> configure
)
```

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `endpoints` | `IEndpointRouteBuilder` | Yes | N/A | Typically `WebApplication` |
| `path` | `string` | No (first overload) | `"/hubs/trax-events"` | URL where clients open the SignalR connection |
| `configure` | `Action<TraxTrainEventHubOptions>` | Yes | N/A | Chooses the authorization posture, and optionally adjusts the connection options |

## Authorization

`TraxTrainEventHubOptions` has no default posture. Choose one:

| Method | Parameters | Description |
|--------|------------|-------------|
| `RequireAuthorization` | `params string[] policies` | Admits callers the host's authorization accepts. With no arguments, a caller must satisfy the host's default policy and, when the host sets a `FallbackPolicy`, that too. With policy names, a caller must satisfy every one, and each must be registered on the host. Repeated calls add policies. |
| `RequireRoles` | `params string[] roles` | Admits authenticated callers in at least one of the roles. Throws `ArgumentException` with no roles, or with a role name containing a comma: pass each role as its own argument. Combines with `RequireAuthorization`, and then both apply. |
| `AllowAnonymous` | none | Opens the hub to any client that can reach it, overriding a fallback policy on the host. Logs a `Warning` at startup. For a hub reachable only from a trusted network. |
| `AllowOrigins` | `params string[] origins` | Browser origins, besides the hub's own, allowed to reach the hub (negotiate and every transport). Each is a scheme and host, with a port if not the default (`https://app.example.com`); matching ignores case and a spelled-out default port. Repeated calls add origins. With no arguments only the hub's own origin is allowed, and the CORS default policy is not consulted. Throws `ArgumentException` for a value that is not an http(s) origin. See [Origins](#origins). |
| `ConfigureConnection` | `Action<HttpConnectionDispatcherOptions>` | Adjusts the hub's connection options after Trax applies its defaults, so a value set here wins, except `CloseOnAuthenticationExpiration` (see [Connection lifetime](#connection-lifetime)). |

`MapTraxTrainEventHub` throws `InvalidOperationException` at startup when `configure` chooses no posture, when it combines `AllowAnonymous()` with `RequireAuthorization` or `RequireRoles`, or when it names a policy the host has not registered. Each policy name is resolved through the host's `IAuthorizationPolicyProvider` where the hub is mapped, so a misspelled name stops the host rather than failing the first connection.

A bare `RequireAuthorization()` keeps the host's fallback policy. ASP.NET Core applies `FallbackPolicy` only to endpoints with no authorization of their own, so an endpoint marked with a bare `[Authorize]` is checked against the default policy alone. The hub adds the fallback policy back, so it is never more open than an endpoint with no annotation: on a host whose default policy is "authenticated" and whose fallback requires the `Operator` role, `RequireAuthorization()` admits only operators. Named policies and roles are applied as given, without the fallback.

The posture is applied to the hub's endpoints (the negotiate request and the connection), so the host's `UseAuthentication()` and `UseAuthorization()` decide who connects. A refused client gets `401` or `403` from the negotiate request, and a SignalR client reports it as an `HttpRequestException` from `StartAsync`. A client that skips negotiation and opens a WebSocket directly is checked on the upgrade request in the same way. Browser clients pass their credential the way the host expects it, for example `accessTokenFactory` in the JavaScript client for a bearer token, which SignalR sends as the `access_token` query parameter on WebSocket requests.

A browser page served by the same host as the hub can use a cookie: the browser sends it on the
negotiate request and on the WebSocket upgrade by itself, so `withUrl("/hubs/trax-events")` needs no
options. Register cookie authentication, sign the user in with a role, map the hub with
`RequireRoles(...)`, and set `SameSite=Strict` on the cookie if any cookie-authenticated endpoint
changes state. Set `OnRedirectToLogin` to answer `401`, or a refused negotiate request is redirected
to a login page instead. The [SignalR Broadcaster sample](/docs/samples/signalr-broadcaster) is built
this way.

The choice and its alternatives are recorded in Trax.Effect's ADR 0016.

## Origins

A request to the hub that carries an `Origin` header, the negotiate request or any transport's, is
refused with `403`, whatever its credentials, unless its origin is:

- on the hub's own host
- allowed by `AllowOrigins(...)`
- without `AllowOrigins`, allowed by the host's CORS default policy
  (`AddCors(o => o.AddDefaultPolicy(...))`, including `AllowAnyOrigin()`)

With neither `AllowOrigins` nor a CORS default policy, only the hub's own origin is allowed. A
request with no `Origin` header, which is what a non-browser client sends, is left to the
authorization posture. The rule applies to an `AllowAnonymous()` hub as well. The scheme is not
compared against the hub's own origin, because TLS is commonly terminated in front of the app. It is
the same rule Trax.Api applies to GraphQL WebSocket upgrades with
[`AllowSocketOrigins`](/docs/sdk-reference/graphql-api/subscriptions#allowed-origins).

```csharp
app.MapTraxTrainEventHub(hub => hub
    .RequireAuthorization("TraxEvents")
    .AllowOrigins("https://dashboard.example.com"));
```

A browser client on another origin also needs CORS for the negotiate request, so the host's CORS
policy admits that origin as well.

## Connection lifetime

Authorization runs when a connection opens. The hub sets `HttpConnectionDispatcherOptions.CloseOnAuthenticationExpiration`, so a connection is closed once the authentication it was admitted on expires, for example when a bearer token's lifetime ends. It is set after `ConfigureConnection` runs, so a host cannot turn it off. A client using `WithAutomaticReconnect()` reconnects, and is authorized again with whatever credential it presents then.

The expiry is the bound: a user whose access is revoked stays connected until the credential they connected with expires. Keep the lifetime of the credentials that reach the hub as short as that delay allows.

Access is all or nothing per connection. A connection the posture admits receives every train's events that the sink's filters let through; the hub does not filter by who started a run.

## Send timeout

The hub's `HttpConnectionDispatcherOptions.TransportSendTimeout` is set to `SignalRHubEndpointExtensions.DefaultTransportSendTimeout`, 2 seconds, instead of ASP.NET Core's 10. A client that cannot take a send within that time is disconnected, and a client using `WithAutomaticReconnect()` reconnects. The timeout bounds how long one slow client can delay delivery to the others (see [UseSignalRHub: Delivery](/docs/sdk-reference/configuration/use-signalr-hub#delivery)); it never delays a train, which does not wait for clients at all.

To change it, use `ConfigureConnection`:

```csharp
app.MapTraxTrainEventHub(hub => hub
    .RequireAuthorization("TraxEvents")
    .ConfigureConnection(o => o.TransportSendTimeout = TimeSpan.FromSeconds(5)));
```

## Prerequisites

The host must register SignalR services, and the authentication and authorization its posture relies on:

```csharp
builder.Services.AddSignalR();
builder.Services.AddAuthentication(/* your scheme */);
builder.Services.AddAuthorization(o =>
    o.AddPolicy("TraxEvents", p => p.RequireRole("Operator")));
```

If `AddSignalR()` is missing, `MapTraxTrainEventHub` throws `InvalidOperationException` with guidance at startup. This fails fast before the host accepts requests rather than letting client connections fail later.

## Example

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddAuthorization(o =>
    o.AddPolicy("TraxEvents", p => p.RequireRole("Operator")));
builder.Services.AddTrax(trax =>
    trax.AddEffects(effects =>
        effects
            .UsePostgres(connStr)
            .UseBroadcaster(b => b.UseSignalRHub())));

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapTraxTrainEventHub(hub => hub.RequireAuthorization("TraxEvents"));   // "/hubs/trax-events"
// or, on a custom path
app.MapTraxTrainEventHub("/internal/trax-events", hub => hub.RequireRoles("Operator"));
// or, deliberately open, on a trusted network only
app.MapTraxTrainEventHub(hub => hub.AllowAnonymous());
app.Run();
```

## Client examples

### Blazor (C#)

```razor
@inject NavigationManager Nav
@implements IAsyncDisposable

@code {
    private HubConnection? _hub;

    protected override async Task OnInitializedAsync()
    {
        _hub = new HubConnectionBuilder()
            .WithUrl(
                Nav.ToAbsoluteUri("/hubs/trax-events"),
                o => o.AccessTokenProvider = () => GetAccessTokenAsync())
            .WithAutomaticReconnect()
            .Build();

        _hub.On<TraxClientEvent>("TrainEvent", async evt =>
        {
            // update component state, then re-render
            await InvokeAsync(StateHasChanged);
        });

        await _hub.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_hub is not null) await _hub.DisposeAsync();
    }
}
```

### JavaScript / TypeScript

```ts
import { HubConnectionBuilder } from "@microsoft/signalr";

const connection = new HubConnectionBuilder()
    .withUrl("/hubs/trax-events", { accessTokenFactory: () => getAccessToken() })
    .withAutomaticReconnect()
    .build();

connection.on("TrainEvent", (evt) => {
    // evt: { metadataId, externalId, trainName, eventType, timestamp }
});

await connection.start();
```

### Testing the posture

`Microsoft.AspNetCore.SignalR.Client` can join the hub through `WebApplicationFactory` by using the
test server's handler and long polling. A refused connection throws from `StartAsync`:

```csharp
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

var connection = new HubConnectionBuilder()
    .WithUrl(new Uri(factory.Server.BaseAddress, "/hubs/trax-events"), options =>
    {
        options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
        options.Transports = HttpTransportType.LongPolling;
        options.Headers["Cookie"] = cookie;   // or an Authorization header; omit it to test refusal
    })
    .Build();

var refused = await FluentActions.Awaiting(() => connection.StartAsync())
    .Should().ThrowAsync<HttpRequestException>();
refused.Which.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
```

Register one handler per client method: the .NET client binds a method's arguments with the
parameter types of the first handler registered for it.

## Payload shape

By default the hub sends a `TraxClientEvent` per matching lifecycle event. The shape is configurable via [`WithProjection`](/docs/sdk-reference/configuration/use-signalr-hub#options). See [UseSignalRHub](/docs/sdk-reference/configuration/use-signalr-hub#default-projection) for the field list.

## SDK Reference

> [UseSignalRHub](/docs/sdk-reference/configuration/use-signalr-hub) | [UseBroadcaster](/docs/sdk-reference/configuration/use-broadcaster)
