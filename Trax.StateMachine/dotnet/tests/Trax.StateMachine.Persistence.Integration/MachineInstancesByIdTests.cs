using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Core.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Sqlite.Extensions;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.StateMachine.Features;
using Trax.StateMachine.Persistence.FromIr;

namespace Trax.StateMachine.Persistence.Integration;

/// <summary>
/// Host code starts and advances a machine served from its IR by the machine's id, since its type is emitted at
/// startup and no code can name it. An id the host does not serve is refused, and so is a user-owned machine's
/// (ADR <c>Trax.Docs/adr/0058</c>).
/// </summary>
public class MachineInstancesByIdTests
{
    private string? _dir;
    private string? _file;
    private ServiceProvider? _provider;

    [SetUp]
    public async Task SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"trax-by-id-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir.NotNull());
        _file = Path.Combine(Path.GetTempPath(), $"sm_by_id_{Guid.NewGuid():N}.db");
        var (irs, host) = PolicyRunnerTests.Definitions();
        foreach (var (id, ir) in irs)
            await File.WriteAllTextAsync(
                    Path.Combine(_dir.NotNull(), id + IrDirectory.IrSuffix),
                    ir.ToJsonString()
                )
                .ConfigureAwait(false);
        await File.WriteAllTextAsync(
                Path.Combine(_dir.NotNull(), IrDirectory.HostFileName),
                host.ToJsonString()
            )
            .ConfigureAwait(false);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(e => e.UseSqlite($"Data Source={_file.NotNull()}"))
                .AddStateMachines(o => o.ServeFeatures(_dir.NotNull()))
        );
        _provider = services.BuildServiceProvider();
        foreach (
            var service in _provider
                .NotNull()
                .GetServices<IHostedService>()
                .OfType<IHostedLifecycleService>()
        )
            await service.StartingAsync(CancellationToken.None).ConfigureAwait(false);
    }

    [TearDown]
    public async Task TearDown()
    {
        if (_provider is not null)
            await _provider.DisposeAsync();
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dir.NotNull(), recursive: true);
        try
        {
            File.Delete(_file.NotNull());
        }
        catch (IOException)
        {
            // Best effort: a lingering handle can hold the temp file; the OS reaps it later.
        }
    }

    [Test]
    public async Task Host_code_advances_a_served_machine_by_its_id_and_the_history_records_the_host()
    {
        var scope = _provider.NotNull().CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        var instances = scope.ServiceProvider.GetRequiredService<IMachineInstances>();

        var outcome = await instances.Advance("p-shop", "main", "Open");

        outcome
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>()
            .Which.Snapshot.State.Should()
            .Be("Opened");
        var id = SystemInstanceStarter.IdOf("p-shop", "p-shop/main");
        var data = scope.ServiceProvider.GetRequiredService<IDataContext>();
        var row = await data.SnapshotDrafts.AsNoTracking().SingleAsync(d => d.Id == id);
        var last = await data
            .SnapshotTransitions.AsNoTracking()
            .Where(t => t.DraftRowId == row.RowId)
            .OrderByDescending(t => t.Revision)
            .FirstAsync();
        last.Trigger.Should().Be("Open");
        last.Actor.Should().Be(SnapshotTransitionActor.Host);
    }

    [Test]
    public async Task Host_code_starts_a_served_machine_by_its_id_at_its_key()
    {
        var scope = _provider.NotNull().CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        var instances = scope.ServiceProvider.GetRequiredService<IMachineInstances>();

        var started = await instances.Start("p-shop", "main");

        started.Created.Should().BeFalse("the host started it");
        started.State.Should().Be("Closed");
        started.Id.Should().Be(SystemInstanceStarter.IdOf("p-shop", "p-shop/main"));
    }

    [Test]
    public async Task A_system_machine_at_a_fixed_key_is_refused_at_another()
    {
        var scope = _provider.NotNull().CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        var instances = scope.ServiceProvider.GetRequiredService<IMachineInstances>();

        var start = () => instances.Start("p-shop", "second");
        var advance = () => instances.Advance("p-shop", "second", "Open");

        (await start.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should()
            .Contain("has one instance, at (\"main\")");
        (await advance.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should()
            .Contain("has one instance, at (\"main\")");
        (
            await scope
                .ServiceProvider.GetRequiredService<IDataContext>()
                .SnapshotDrafts.AsNoTracking()
                .CountAsync(d => d.Machine == "p-shop")
        )
            .Should()
            .Be(1);
    }

    [Test]
    public async Task An_id_the_host_does_not_serve_is_refused()
    {
        var scope = _provider.NotNull().CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        var instances = scope.ServiceProvider.GetRequiredService<IMachineInstances>();

        var advance = () => instances.Advance("p-nothing", "main", "Open");
        var start = () => instances.Start("p-nothing", "main");

        (await advance.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should()
            .Contain("No machine 'p-nothing' is served");
        await start.Should().ThrowAsync<InvalidOperationException>();
    }

    [Test]
    public async Task A_user_owned_machine_is_refused_by_its_id()
    {
        var scope = _provider.NotNull().CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        var instances = scope.ServiceProvider.GetRequiredService<IMachineInstances>();

        var start = () => instances.Start("p-order", "main", new JsonObject());

        (await start.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should()
            .Contain("user-owned");
    }
}
