using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Api.Auth;
using Trax.Api.Auth.ApiKey;
using Trax.Api.Extensions;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.GraphQL.PersistedOperations.Extensions;
using Trax.Effect.Attributes;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Progress.Extensions;
using Trax.Effect.StateMachine;
using Trax.Effect.StateMachine.Persistence;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Extensions;
using static Trax.Effect.StateMachine.Rules;

namespace Trax.Api.Tests.AuthE2E;

/// <summary>
/// A run a user's machine instance invoked is followed by the instance's owner through <c>runProgress</c> after the
/// instance itself is gone: the run records its starter, so the owner needs no draft to read it. The host runs the
/// scheduler, so the run is dispatched and held at <see cref="ProgressMarkerTrain"/>'s gate while the draft expires.
/// </summary>
/// <remarks>
/// The idle TTL never deletes a draft that holds a live run, so the draft goes by its lifetime, which cancels the run
/// first. The run then ends cancelled at its next junction, and that end is what the owner follows it to.
/// <para>Enforces <c>Trax.Docs/adr/0051-only-the-caller-who-started-a-run-may-follow-its-progress.md</c>.</para>
/// </remarks>
[TestFixture]
[NonParallelizable]
[Property("adr", Adr)]
public class RunProgressInvokedRunE2ETests
{
    private const string Adr =
        "Trax.Docs/adr/0051-only-the-caller-who-started-a-run-may-follow-its-progress.md";

    private const string Database = "trax_api_run_progress_invoked";
    private const string AliceKey = "invoked-alice-key";
    private const string BobKey = "invoked-bob-key";

    private const string EventFields = "sequence state problem { code }";

    private IHost _host = null!;

    [OneTimeSetUp]
    public async Task StartHost()
    {
        AuthE2EHost.EnsureDatabaseExists(Database);
        // The scheduler's workers and the machines' listeners need more than the AuthE2E pool of 8.
        var connectionString = AuthE2EHost
            .ConnectionString(Database)
            .Replace("Maximum Pool Size=8", "Maximum Pool Size=20");

        _host = new HostBuilder()
            .ConfigureWebHost(web =>
                web.UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddLogging();
                        services.AddRouting();
                        services.AddTraxApiKeyAuth(keys =>
                            keys.Add(AliceKey, id: "alice", "Player")
                                .Add(BobKey, id: "bob", "Player")
                        );
                        services.AddAuthorization(o =>
                            o.AddPolicy("AdminPolicy", p => p.RequireRole("Admin"))
                        );
                        services.AddTrax(trax =>
                            trax.AddEffects(effects =>
                                    effects
                                        .UsePostgres(connectionString)
                                        .AddDecisionRecording()
                                        .AddJunctionProgress()
                                )
                                .AddStateMachines(o =>
                                {
                                    o.DraftTtl = TimeSpan.FromMinutes(1);
                                    o.MaxDraftLifetime = TimeSpan.FromMinutes(2);
                                })
                                .AddMediator(typeof(AuthE2EHost).Assembly)
                                .AddScheduler(scheduler => scheduler)
                        );
                        services.AddSingleton<IMachine, ErrandMachine>();
                        services.AddScoped<ISnapshotPrincipal, CallerPrincipal>();
                        services.AddTraxApi();
                        // A served machine needs persisted operations; inline documents are still served.
                        services.AddTraxGraphQL(graphql =>
                            graphql.UsePersistedOperations(po =>
                                po.SingleNode()
                                    .RequirePersisted(false)
                                    .LogNonPersistedRequests(true)
                            )
                        );
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseAuthentication();
                        app.UseAuthorization();
                        app.UseWebSockets();
                        app.UseEndpoints(e => e.MapGraphQL("/trax/graphql", "trax"));
                    })
            )
            .Build();
        await _host.StartAsync();
    }

    [OneTimeTearDown]
    public async Task StopHost()
    {
        ProgressMarkerTrain.Gate.TrySetResult();
        await _host.StopAsync();
        _host.Dispose();
    }

    [Test]
    public async Task A_machine_invoked_run_reaches_the_instance_owner_after_its_draft_is_swept()
    {
        ProgressMarkerTrain.Reset(open: false);
        var id = await StartAsync(AliceKey);
        var runId = await GoAsync(AliceKey, id);
        await ProgressMarkerTrain.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(30));

        // Older than its lifetime, then swept: the draft goes, and its run is asked to cancel.
        await ExpireAsync(id);
        await _host.Services.GetRequiredService<DraftSweeper>().SweepOnce();
        (await DraftExistsAsync(id)).Should().BeFalse("the draft outlived its lifetime");

        (await ReadAsync(runId, AliceKey))
            .GetProperty("state")
            .GetString()
            .Should()
            .Be(
                "IN_PROGRESS",
                "the run records its starter, so the owner needs no draft, per " + Adr
            );
        (await ReadAsync(runId, BobKey))
            .GetProperty("problem")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be("NOT_FOUND");

        await using var alice = await RunProgressE2ETests.Socket.ConnectAsync(_host, AliceKey);
        await alice.SubscribeAsync(
            $$"""subscription { runProgress(runId: "{{runId}}") { {{EventFields}} } }"""
        );
        (await alice.NextAsync()).GetProperty("state").GetString().Should().Be("IN_PROGRESS");

        ProgressMarkerTrain.Gate.TrySetResult();
        var events = await alice.ReadUntilCompleteAsync();

        events
            .Should()
            .NotBeEmpty()
            .And.Subject.Last()
            .GetProperty("state")
            .GetString()
            .Should()
            .Be("CANCELLED", "the swept draft's run was cancelled, and its owner sees it end");
        events.Should().OnlyContain(e => e.GetProperty("problem").ValueKind == JsonValueKind.Null);
        alice.Received.Should().NotContain(RunProgressE2ETests.Marker);
    }

    #region Helpers

    private async Task<Guid> StartAsync(string apiKey)
    {
        using var doc = await _host.PostGraphQLAsync(
            $$"""mutation { machines { errand { start(input: { note: "{{RunProgressE2ETests.Marker}}" }, requestId: "{{Guid.NewGuid():N}}") { id problem { code message } } } } }""",
            r => r.Headers.Add("X-Api-Key", apiKey)
        );
        var start = Answer(doc, "start");
        start.GetProperty("problem").ValueKind.Should().Be(JsonValueKind.Null, start.GetRawText());
        return Guid.Parse(start.GetProperty("id").GetString()!);
    }

    // Enters the invoking state and answers the run it queued, as its owner reads it from the event's answer.
    private async Task<string> GoAsync(string apiKey, Guid id)
    {
        using var doc = await _host.PostGraphQLAsync(
            $$"""mutation { machines { errand { go(id: "{{id}}", requestId: "{{Guid.NewGuid():N}}") { state invokedRun { runId } problem { code message } } } } }""",
            r => r.Headers.Add("X-Api-Key", apiKey)
        );
        var go = Answer(doc, "go");
        go.GetProperty("state").GetString().Should().Be("Running", go.GetRawText());
        return go.GetProperty("invokedRun").GetProperty("runId").GetString()!;
    }

    private static JsonElement Answer(JsonDocument doc, string field)
    {
        doc.RootElement.TryGetProperty("errors", out var errors)
            .Should()
            .BeFalse(errors.ToString());
        return doc
            .RootElement.GetProperty("data")
            .GetProperty("machines")
            .GetProperty("errand")
            .GetProperty(field)
            .Clone();
    }

    private async Task<JsonElement> ReadAsync(string runId, string apiKey)
    {
        using var doc = await _host.PostGraphQLAsync(
            $$"""{ runProgress(runId: "{{runId}}") { runId state problem { code } } }""",
            r => r.Headers.Add("X-Api-Key", apiKey)
        );
        doc.RootElement.TryGetProperty("errors", out var errors)
            .Should()
            .BeFalse(errors.ToString());
        return doc.RootElement.GetProperty("data").GetProperty("runProgress").Clone();
    }

    // The store stamps every write with now; this makes the draft look created and last written an hour ago.
    private async Task ExpireAsync(Guid id)
    {
        var past = DateTimeOffset.UtcNow.AddHours(-1);
        await using var db = await _host
            .Services.GetRequiredService<IDataContextProviderFactory>()
            .CreateDbContextAsync(CancellationToken.None);
        await db
            .SnapshotDrafts.Where(x => x.Id == id && x.Machine == ErrandMachine.MachineId)
            .ExecuteUpdateAsync(s =>
                s.SetProperty(x => x.CreatedAt, past).SetProperty(x => x.UpdatedAt, past)
            );
    }

    private async Task<bool> DraftExistsAsync(Guid id)
    {
        await using var db = await _host
            .Services.GetRequiredService<IDataContextProviderFactory>()
            .CreateDbContextAsync(CancellationToken.None);
        return await db
            .SnapshotDrafts.AsNoTracking()
            .AnyAsync(x => x.Id == id && x.Machine == ErrandMachine.MachineId);
    }

    /// <summary>The snapshot user key is the authenticated caller's principal id.</summary>
    private sealed class CallerPrincipal(TraxCaller caller) : ISnapshotPrincipal
    {
        public string? CurrentUserKey => caller.IsAuthenticated ? caller.Principal!.Id : null;
    }

    #endregion
}

public enum ErrandState
{
    Idle,
    Running,
    Done,
    Failed,
    Cancelled,
}

public enum ErrandTrigger
{
    Go,
}

public sealed record ErrandContext
{
    public string? Note { get; init; }

    public string? Echo { get; init; }
}

public sealed record ErrandStartInput
{
    public string Note { get; init; } = "";
}

/// <summary>
/// A user's errand: <c>Go</c> enters <c>Running</c>, which invokes <see cref="IProgressMarkerTrain"/>. Registered
/// only by <see cref="RunProgressInvokedRunE2ETests"/>.
/// </summary>
[TraxMachine]
public sealed class ErrandMachine : Machine<ErrandState, ErrandTrigger>
{
    public const string MachineId = "errand";

    protected override void Configure(IMachineBuilder<ErrandState, ErrandTrigger> m)
    {
        m.Id(MachineId).Version(1).StartsAt(ErrandState.Idle, () => new JsonObject());
        m.Start<ErrandStartInput>(s =>
            s.Reduce(Set((ErrandContext c) => c.Note).FromInput((ErrandStartInput i) => i.Note))
                .AnyAuthenticated()
        );
        m.Get(r => r.OwnerOnly());
        m.Watch(r => r.OwnerOnly());
        m.Discard(r => r.OwnerOnly());

        m.In(ErrandState.Idle)
            .Context<ErrandContext>()
            .On(ErrandTrigger.Go)
            .AnyAuthenticated()
            .To(ErrandState.Running);

        m.In(ErrandState.Running)
            .Context<ErrandContext>()
            .Invokes<IProgressMarkerTrain, ProgressMarkerInput, ProgressMarkerOutput>(
                ctx => new ProgressMarkerInput { Note = ctx["note"]?.GetValue<string>() ?? "" }
            )
            .OnDone(
                ErrandState.Done,
                reduce: Set((ErrandContext c) => c.Echo)
                    .FromInput((ProgressMarkerOutput o) => o.Echo)
            )
            .OnFailed(ErrandState.Failed)
            .OnCancelled(ErrandState.Cancelled);

        m.In(ErrandState.Done).Context<ErrandContext>();
        m.In(ErrandState.Failed).Context<ErrandContext>();
        m.In(ErrandState.Cancelled).Context<ErrandContext>();
    }
}
