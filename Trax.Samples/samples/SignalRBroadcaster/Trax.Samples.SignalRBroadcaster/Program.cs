// ─────────────────────────────────────────────────────────────────────────────
// Trax SignalR broadcaster: live train events in a browser, behind a sign-in
//
// One process. Every train it runs raises lifecycle events (Started, Completed,
// Failed); UseBroadcaster(b => b.UseSignalRHub(...)) pushes each one to the
// clients connected to the hub MapTraxTrainEventHub maps at /hubs/trax-events.
// The page at / (wwwroot/index.html) is a plain browser client.
//
// The hub sends every train's events to every client it admits, so it must say
// who may connect, or the host does not start. Here: signed-in operators only
// (RequireRoles). The browser signs in with a cookie, which it sends on the
// SignalR negotiate request and the WebSocket upgrade by itself, so the page
// needs no token code. The demo sign-in exists only in Development.
//
// The default event payload has no failure reason. This sample projects its own
// (LiveTrainEvent), which carries the reason only for a TrainException.
//
// Run it (from Trax.Samples/; no database needed, effects are in memory):
//   dotnet run --project samples/SignalRBroadcaster/Trax.Samples.SignalRBroadcaster
//
// Then open http://localhost:5270, sign in, and press the buttons.
//
// Try it without a browser:
//   curl -i -X POST http://localhost:5270/pings          # 401: not signed in
//   curl -i http://localhost:5270/hubs/trax-events/negotiate?negotiateVersion=1 -X POST   # 401
//
// Docs: https://traxsharp.net/docs/samples/signalr-broadcaster
// ─────────────────────────────────────────────────────────────────────────────

using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Trax.Effect.Broadcaster.SignalR.Extensions;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Mediator.Services.TrainBus;
using Trax.Samples.SignalRBroadcaster;
using Trax.Samples.SignalRBroadcaster.Trains.Ping;
using Trax.Samples.SignalRBroadcaster.Workarounds;

const string OperatorRole = "Operator";

var builder = WebApplication.CreateBuilder(args);

// ── Who may watch ───────────────────────────────────────────────────────
// Cookie sign-in. SameSite=Strict keeps the cookie off cross-site requests, so another site cannot
// make a signed-in browser POST /pings. An API caller is answered 401/403, not redirected.
builder
    .Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(cookie =>
    {
        cookie.Cookie.SameSite = SameSiteMode.Strict;
        cookie.Cookie.HttpOnly = true;
        cookie.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        cookie.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();

// The page posts {"outcome":"FailForClients"}: accept enum names.
builder.Services.ConfigureHttpJsonOptions(json =>
    json.SerializerOptions.Converters.Add(new JsonStringEnumConverter())
);

// SignalR itself: MapTraxTrainEventHub refuses to start without it.
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

// TEMPORARY: works around a Trax.Effect defect that stops the SignalR sink after the first train
// run. Delete this line and Workarounds/SignalRSinkKeepAlive.cs once Trax.Effect ships the fix.
builder.Services.KeepSignalRSinkAlive();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

// The hub: operators only. A client without the role gets 401/403 from the negotiate request.
app.MapTraxTrainEventHub(hub => hub.RequireRoles(OperatorRole));

// Who am I? The page uses it to show the sign-in button or the feed.
app.MapGet(
    "/me",
    (ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true
            ? Results.Ok(new { name = user.Identity.Name })
            : Results.Unauthorized()
);

// Run a ping; its events reach the page through the hub, not through this response.
app.MapPost(
        "/pings",
        async (PingRequest request, ITrainBus trains, ILogger<PingRequest> logger) =>
        {
            try
            {
                await trains.RunAsync<PingOutput>(
                    new PingInput { Source = "page", Outcome = request.Outcome }
                );
            }
            catch (Exception exception)
            {
                // A failing ping is what two of the buttons are for: the page shows the failure from
                // the hub, and the full reason stays here, in the server log.
                logger.LogWarning(exception, "Ping ({Outcome}) failed", request.Outcome);
            }

            return Results.Accepted();
        }
    )
    .RequireAuthorization(policy => policy.RequireRole(OperatorRole));

app.MapPost(
    "/sign-out",
    async (HttpContext context) =>
    {
        await context.SignOutAsync();
        return Results.Redirect("/");
    }
);

// ── Demo sign-in: Development only ─────────────────────────────────────
// Signs the browser in as an operator without a password. It is mapped only in Development
// (`dotnet run` starts there through Properties/launchSettings.json); anywhere else there is no way
// to sign in until you add a real one, so the hub admits nobody.
if (app.Environment.IsDevelopment())
    app.MapPost(
        "/demo/sign-in",
        async (HttpContext context) =>
        {
            var identity = new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.Name, "demo-operator"),
                    new Claim(ClaimTypes.Role, OperatorRole),
                ],
                CookieAuthenticationDefaults.AuthenticationScheme
            );
            await context.SignInAsync(new ClaimsPrincipal(identity));
            return Results.Redirect("/");
        }
    );

app.Run();

/// <summary>The body of <c>POST /pings</c>.</summary>
public record PingRequest(PingOutcome Outcome = PingOutcome.Succeed);

namespace Trax.Samples.SignalRBroadcaster
{
    public partial class Program;
}
