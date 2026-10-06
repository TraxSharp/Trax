---
layout: default
title: SignalR Broadcaster
description: "The SignalRBroadcaster sample: live train events in a browser through the SignalR sink, a hub only signed-in operators may join, and a projected failure reason."
parent: Samples & Deployment
nav_order: 11
---

# SignalR Broadcaster

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for securing systems that use it. See [API Security](/docs/api-security).

`samples/SignalRBroadcaster` in [Trax.Samples](https://github.com/TraxSharp/Trax.Samples) pushes each
train's lifecycle events to a browser as they happen. It is one process with one train, a plain HTML
page using the JavaScript SignalR client, and a hub that only a signed-in operator may join. Effects
are in memory, so it needs no database.

## What it proves

| Feature | Where |
|---|---|
| `UseBroadcaster(b => b.UseSignalRHub(...))` and `MapTraxTrainEventHub(...)` are the whole server side | `Program.cs` |
| The hub refuses a client that is not signed in, with `401` on negotiate | `HubPostureTests` |
| A browser cookie as the hub credential, sent on negotiate and on the WebSocket with no client code | `Program.cs`, `wwwroot/index.html` |
| A custom projection that sends a failure reason only when the train wrote it for clients | `LiveTrainEvent.cs`, `LiveEventTests` |
| The demo sign-in exists only in Development | `HubPostureTests.Outside_development_there_is_no_way_to_sign_in` |

## Run

From the `Trax.Samples` root:

```bash
dotnet run --project samples/SignalRBroadcaster/Trax.Samples.SignalRBroadcaster
```

Open <http://localhost:5270>. `dotnet run` starts in Development through
`Properties/launchSettings.json`, the only environment that maps the demo sign-in.

## Try it

1. Press **Try to connect without signing in**: the hub's negotiate request answers `401`, and no connection
   opens. Then press **Sign in as the demo operator**; the page connects to `/hubs/trax-events`.
2. Press **A ping that succeeds**: a run card appears and fills in live, `Started` then `Completed`, with the time
   between them.
3. Press **A ping that fails, for readers**: the run fails with a `TrainException`, and its card shows the reason
   `The ping target did not answer.`
4. Press **A ping that fails, internally**: the card shows the reason withheld,
   `The run failed. The reason is in the server log.`, and the server's console shows the real message,
   which names an internal host and user.

**Show the raw traffic** lists what the page sends and every `TrainEvent` the hub pushes. `POST /pings` answers
`202 Accepted` and nothing else: the run's events reach the page over the hub.

Without signing in, both of these answer `401`:

```bash
curl -i -X POST http://localhost:5270/pings
curl -i -X POST "http://localhost:5270/hubs/trax-events/negotiate?negotiateVersion=1"
```

## How it works

### The server

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;          // SignInAsync
using Microsoft.AspNetCore.Authentication.Cookies;
using Trax.Effect.Broadcaster.SignalR.Extensions;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;

builder
    .Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(cookie =>
    {
        cookie.Cookie.SameSite = SameSiteMode.Strict;
        cookie.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddSignalR();

builder.Services.AddTrax(trax =>
    trax.AddEffects(effects =>
            effects
                .UseInMemory()
                .UseBroadcaster(broadcaster =>
                    broadcaster.UseSignalRHub(hub =>
                        hub.OnlyForEvents("Started", "Completed", "Failed")
                            .OnlyForTrains<IPingTrain>()
                            .WithProjection(LiveTrainEvent.From)
                    )
                )
        )
        .AddMediator(typeof(IPingTrain).Assembly)
);

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.MapTraxTrainEventHub(hub => hub.RequireRoles("Operator"));
```

`UseSignalRHub` is a sink, not a transport: with no RabbitMQ it serves the trains this process runs.
Add `UseRabbitMq(...)` beside it and events from workers in other processes reach the same hub (see
[Broadcaster Sinks](/docs/effect/broadcaster-sinks)).

The hub sends every matching event to every client it admits, so `MapTraxTrainEventHub` will not
start without a posture. `RequireRoles("Operator")` admits authenticated callers in that role; the
negotiate request of anyone else gets `401` (not signed in) or `403` (signed in without the role).
`AddSignalR()` is required: without it `MapTraxTrainEventHub` throws at startup. See
[MapTraxTrainEventHub](/docs/sdk-reference/configuration/map-trax-train-event-hub#authorization).

### The browser

```html
<script src="https://cdn.jsdelivr.net/npm/@microsoft/signalr@8.0.7/dist/browser/signalr.min.js"
        integrity="sha384-mU1xC5yC2LldSW74Rj1Ax8wPiLw/28V5eh51uKJMlBbRVsOtUYd4xyzNsgIAJARB"
        crossorigin="anonymous"></script>
<script>
  const hub = new signalR.HubConnectionBuilder()
    .withUrl("/hubs/trax-events")
    .withAutomaticReconnect()
    .build();

  hub.on("TrainEvent", (evt) => {
    // evt is the projection: { externalId, trainName, eventType, timestamp, failureReason }
  });

  await hub.start();
</script>
```

The page is served by the same host, so the browser sends the sign-in cookie on the negotiate request
and on the WebSocket upgrade by itself; there is no token code. A page on another origin, or a
bearer-token API, passes its credential with `accessTokenFactory` instead. `SameSite=Strict` keeps the
cookie off requests another site starts, which is what makes the cookie-authenticated `POST /pings`
safe from cross-site forgery.

The sign-in itself is a demo endpoint, mapped only in Development:

```csharp
if (app.Environment.IsDevelopment())
    app.MapPost("/demo/sign-in", async (HttpContext context) =>
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "demo-operator"), new Claim(ClaimTypes.Role, "Operator")],
            CookieAuthenticationDefaults.AuthenticationScheme
        );
        await context.SignInAsync(new ClaimsPrincipal(identity));
        return Results.Redirect("/");
    });
```

Anywhere else nobody can sign in, so the hub admits nobody until you add a real sign-in.

### Why the default payload has no failure reason

The default projection, `TraxClientEvent`, carries the run's ids, train name, event type and timestamp,
and leaves `FailureReason` off the wire. A failure reason is the text of the exception the failing
code threw. It can name a host, a user or a credential, and the hub sends every train's events to
every client it admits, whoever started the run. So the sink sends none unless you choose to.

This sample chooses the same rule GraphQL subscriptions use: show the message only when the run failed
with a `TrainException`, the type a train author throws for a message meant to be read.

```csharp
using Trax.Core.Exceptions;
using Trax.Effect.Services.TrainEventBroadcaster;

public sealed record LiveTrainEvent(
    string ExternalId,
    string TrainName,
    string EventType,
    DateTime Timestamp,
    string? FailureReason
)
{
    public const string MaskedReason = "The run failed. The reason is in the server log.";

    public static LiveTrainEvent From(TrainLifecycleEventMessage message) =>
        new(
            message.ExternalId,
            message.TrainName[(message.TrainName.LastIndexOf('.') + 1)..],
            message.EventType,
            message.Timestamp,
            message.EventType == "Failed" ? ClientReason(message) : null
        );

    private static string ClientReason(TrainLifecycleEventMessage message) =>
        message.FailureException == nameof(TrainException) && message.FailureReason is not null
            ? message.FailureReason
            : MaskedReason;
}
```

`FailureException` is the short type name of the exception the run recorded (`TrainException`,
`InvalidOperationException`). A subclass of `TrainException` reports its own name, so list it too if
you throw one. See [UseSignalRHub: Default projection](/docs/sdk-reference/configuration/use-signalr-hub#default-projection).

### A temporary workaround in the sample

`Workarounds/SignalRSinkKeepAlive.cs` is not part of the pattern. The current Trax.Effect disposes the
shared SignalR sink when the first train run ends, after which the hub delivers nothing; the file keeps
the sink alive until that is fixed upstream. Do not copy it.

## Tests

```bash
dotnet test tests/Trax.Samples.SignalRBroadcaster.E2E
```

A .NET SignalR client (`Microsoft.AspNetCore.SignalR.Client`) joins the hub through
`WebApplicationFactory`, using the test server's handler and long polling, and carrying the cookie the
demo sign-in returned:

```csharp
var connection = new HubConnectionBuilder()
    .WithUrl(
        new Uri(factory.Server.BaseAddress, "/hubs/trax-events"),
        options =>
        {
            options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
            options.Transports = HttpTransportType.LongPolling;
            options.Headers["Cookie"] = cookie;
        }
    )
    .Build();
connection.On<LiveTrainEvent>("TrainEvent", received.Enqueue);
await connection.StartAsync();
```

| Test class | Proves |
|---|---|
| `HubPostureTests` | No cookie: `StartAsync` throws `HttpRequestException` with `401`. With the cookie: connected. `POST /pings` refuses an anonymous caller. Production maps no sign-in. |
| `LiveEventTests` | A ping's `Started` and `Completed` arrive live; a `TrainException` shows its message; any other failure shows the masked sentence and none of the real message |

## SDK Reference

> [UseSignalRHub](/docs/sdk-reference/configuration/use-signalr-hub) | [MapTraxTrainEventHub](/docs/sdk-reference/configuration/map-trax-train-event-hub) | [UseBroadcaster](/docs/sdk-reference/configuration/use-broadcaster) | [TrainException](/docs/sdk-reference/trains-and-junctions/train-exception)
