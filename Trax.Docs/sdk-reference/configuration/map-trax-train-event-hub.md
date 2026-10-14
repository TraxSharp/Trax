---
title: MapTraxTrainEventHub
description: Reference for MapTraxTrainEventHub, which maps the Trax SignalR hub behind a required authorization posture, with connection lifetime, send timeout and clients.
type: reference
area: effects
parent: Configuration
grand_parent: SDK Reference
nav_order: 54
package: Trax.Effect.Broadcaster.SignalR
namespace:
  - Trax.Effect.Broadcaster.SignalR.Extensions
  - Trax.Effect.Broadcaster.SignalR.Configuration.TraxTrainEventHubOptions
source:
  - Trax.Effect/src/Trax.Effect.Broadcaster.SignalR/Extensions/SignalRHubEndpointExtensions.cs
  - Trax.Effect/src/Trax.Effect.Broadcaster.SignalR/Configuration/TraxTrainEventHubOptions
---

# MapTraxTrainEventHub

Maps the Trax SignalR hub at a URL path. Clients connect here to receive train lifecycle events pushed by the [`UseSignalRHub`](/docs/sdk-reference/configuration/use-signalr-hub) sink. Every connected client receives every train's events, so the hub is mapped with an authorization posture: the host says who may connect, or the host does not start.

## Signature

```csharp fragment
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

## Parameters

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `endpoints` | `IEndpointRouteBuilder` | Yes | N/A | Typically `WebApplication` |
| `path` | `string` | No (first overload) | `"/hubs/trax-events"` | URL where clients open the SignalR connection |
| `configure` | `Action<TraxTrainEventHubOptions>` | Yes | N/A | Chooses the authorization posture, and optionally adjusts the connection options |

## Returns

`HubEndpointConventionBuilder`, for further endpoint conventions.

## Behavior

The host registers SignalR services with `AddSignalR()`, and the authentication and authorization its posture relies
on. Without `AddSignalR()`, `MapTraxTrainEventHub` throws `InvalidOperationException` at startup.

### Authorization

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

A browser page served by the same host can authenticate with a cookie, which the browser sends on the negotiate request and the WebSocket upgrade; see [Push train events to browsers](/docs/effect/push-train-events-to-browsers).

The choice and its alternatives are recorded in Trax.Effect's ADR 0016.

### Origins

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

```csharp fragment
app.MapTraxTrainEventHub(hub => hub
    .RequireAuthorization("TraxEvents")
    .AllowOrigins("https://dashboard.example.com"));
```

A browser client on another origin also needs CORS for the negotiate request, so the host's CORS
policy admits that origin as well.

### Connection lifetime

Authorization runs when a connection opens. The hub sets `HttpConnectionDispatcherOptions.CloseOnAuthenticationExpiration`, so a connection is closed once the authentication it was admitted on expires, for example when a bearer token's lifetime ends. It is set after `ConfigureConnection` runs, so a host cannot turn it off. A client using `WithAutomaticReconnect()` reconnects, and is authorized again with whatever credential it presents then.

The expiry is the bound: a user whose access is revoked stays connected until the credential they connected with expires.

Access is all or nothing per connection. A connection the posture admits receives every train's events that the sink's filters let through; the hub does not filter by who started a run.

### Send timeout

The hub's `HttpConnectionDispatcherOptions.TransportSendTimeout` is set to `SignalRHubEndpointExtensions.DefaultTransportSendTimeout`, 2 seconds, instead of ASP.NET Core's 10. A client that cannot take a send within that time is disconnected, and a client using `WithAutomaticReconnect()` reconnects. The timeout bounds how long one slow client can delay delivery to the others (see [UseSignalRHub: Delivery](/docs/sdk-reference/configuration/use-signalr-hub#delivery)); it never delays a train, which does not wait for clients at all.

`ConfigureConnection` changes it:

```csharp fragment
app.MapTraxTrainEventHub(hub => hub
    .RequireAuthorization("TraxEvents")
    .ConfigureConnection(o => o.TransportSendTimeout = TimeSpan.FromSeconds(5)));
```

### Payload shape

By default the hub sends a `TraxClientEvent` per matching lifecycle event. The shape is configurable via [`WithProjection`](/docs/sdk-reference/configuration/use-signalr-hub#options). See [UseSignalRHub](/docs/sdk-reference/configuration/use-signalr-hub#default-projection) for the field list.

## Example

```csharp compile
using Microsoft.AspNetCore.Authentication.Cookies;

builder.Services.AddSignalR();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie();
builder.Services.AddAuthorization(o => o.AddPolicy("TraxEvents", p => p.RequireRole("Operator")));
builder.Services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
        .UseBroadcaster(b => b.UseSignalRHub()))
    .AddMediator(typeof(IShipOrderTrain).Assembly));

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapTraxTrainEventHub(hub => hub.RequireAuthorization("TraxEvents"));
app.MapTraxTrainEventHub("/internal/trax-events", hub => hub.RequireRoles("Operator"));
app.Run();
```

## See also

- [Push train events to browsers](/docs/effect/push-train-events-to-browsers)
- [UseSignalRHub](/docs/sdk-reference/configuration/use-signalr-hub), [ITraxTrainEventClient](/docs/sdk-reference/configuration/i-trax-train-event-client)
