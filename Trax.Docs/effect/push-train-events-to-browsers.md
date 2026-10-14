---
title: Push train events to browsers
description: How to send train lifecycle events to browser, Blazor and .NET clients through the Trax SignalR hub, authenticate them, and test who the hub admits.
type: how-to
area: effects
nav_order: 24
---

# Push train events to browsers

The SignalR sink pushes each run's lifecycle events to every client the Trax hub admits. Setting it up is three
parts: the sink, the hub endpoint with an authorization posture, and the client.

## 1. Add the sink and map the hub

```csharp compile
using Microsoft.AspNetCore.Authentication.Cookies;
using Trax.Effect.Broadcaster.SignalR.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR();
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.SameSite = SameSiteMode.Strict;
        o.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(builder.Configuration.GetConnectionString("Trax")!)
        .UseBroadcaster(b => b.UseSignalRHub(opts => opts.OnlyForEvents("Completed", "Failed")))
    )
);

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapTraxTrainEventHub(hub => hub.RequireRoles("Operator"));
app.Run();
```

- `MapTraxTrainEventHub` refuses to start without a posture: `RequireAuthorization(...)`, `RequireRoles(...)`, or
  `AllowAnonymous()` for a hub only a trusted network can reach.
- A page served by the same host can use a cookie: the browser sends it on the negotiate request and on the
  WebSocket upgrade by itself. `SameSite=Strict` matters once any cookie-authenticated endpoint changes state, and
  answering `401` instead of redirecting keeps a refused negotiate request from landing on a login page.
- With runs on other processes, add a transport beside the sink (`b.UseRabbitMq(...).UseSignalRHub()`); see
  [Broadcast events between processes](/docs/effect/broadcast-events-between-processes).

## 2. Connect a browser

```ts
import { HubConnectionBuilder } from "@microsoft/signalr";

const connection = new HubConnectionBuilder()
    .withUrl("/hubs/trax-events")
    .withAutomaticReconnect()
    .build();

connection.on("TrainEvent", (evt) => {
    // evt: { metadataId, externalId, trainName, eventType, timestamp }
});

await connection.start();
```

On another origin, pass the credential the host expects, for example
`withUrl(url, { accessTokenFactory: () => getAccessToken() })` for a bearer token, and allow the origin with
`AllowOrigins(...)` on the hub and in the host's CORS policy.

## 3. Or connect from Blazor or .NET

With `Microsoft.AspNetCore.SignalR.Client`:

```razor
@inject NavigationManager Nav
@implements IAsyncDisposable

@code {
    private HubConnection? _hub;

    protected override async Task OnInitializedAsync()
    {
        _hub = new HubConnectionBuilder()
            .WithUrl(Nav.ToAbsoluteUri("/hubs/trax-events"),
                o => o.AccessTokenProvider = () => GetAccessTokenAsync())
            .WithAutomaticReconnect()
            .Build();

        _hub.On<TraxClientEvent>("TrainEvent", async evt => await InvokeAsync(StateHasChanged));

        await _hub.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_hub is not null) await _hub.DisposeAsync();
    }
}
```

Register one handler per client method: the .NET client binds a method's arguments with the parameter types of the
first handler registered for it.

## 4. Test who the hub admits

The .NET client can join the hub through `WebApplicationFactory` with the test server's handler and long polling.
A refused connection throws from `StartAsync`:

```csharp fragment
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

The [SignalR Broadcaster sample](/docs/samples/signalr-broadcaster) is built this way and tests both outcomes.

## See also

- [How event broadcasting works](/docs/effect/how-event-broadcasting-works)

## SDK Reference

> [UseSignalRHub](/docs/sdk-reference/configuration/use-signalr-hub) | [MapTraxTrainEventHub](/docs/sdk-reference/configuration/map-trax-train-event-hub) | [ITraxTrainEventClient](/docs/sdk-reference/configuration/i-trax-train-event-client)
