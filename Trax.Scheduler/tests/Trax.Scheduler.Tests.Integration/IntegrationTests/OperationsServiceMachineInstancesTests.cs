using System.Reflection;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Enums;
using Trax.Effect.Models.SnapshotDraft;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Tests.Integration.Fixtures;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// State-machine instances as the operations surface reads them for the dashboard's State
/// machines page and the API's <c>operations.machineInstances</c>: filtered by machine, state and
/// owner kind, newest first, counted up to a cap, looked up with the owner kind always named, and
/// never carrying a snapshot's context or its owner's key.
///
/// <para>Enforces <c>Trax.Docs/adr/0022-the-dashboard-and-the-api-share-one-operation-per-action.md</c>.</para>
/// </summary>
[Property("adr", "Trax.Docs/adr/0022-the-dashboard-and-the-api-share-one-operation-per-action.md")]
[TestFixture]
public class OperationsServiceMachineInstancesTests : TestSetup
{
    private const string SecretContext = """{"cardNumber":"4111-1111-1111-1111"}""";

    private IOperationsService _operations = null!;
    private string _machine = null!;

    public override async Task TestSetUp()
    {
        await base.TestSetUp();
        _operations = Scope.ServiceProvider.GetRequiredService<IOperationsService>();
        _machine = $"ops-machine-{Guid.NewGuid():N}";
    }

    [TearDown]
    public async Task DeleteDrafts() =>
        await DataContext
            .SnapshotDrafts.Where(x => x.Machine.StartsWith(_machine))
            .ExecuteDeleteAsync();

    [Test]
    public async Task Instances_are_listed_newest_first_with_what_an_operator_may_see()
    {
        var start = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        var older = await Seed(SnapshotOwnerKind.System, "Waiting", start);
        var newer = await Seed(
            SnapshotOwnerKind.User,
            "Waiting",
            start.AddMinutes(5),
            invokeToken: $"run-{Guid.NewGuid():N}"
        );

        var page = await _operations.GetMachineInstancesAsync(
            new MachineInstanceQuery(Machine: _machine),
            CancellationToken.None
        );

        page.Items.Select(i => i.RowId).Should().Equal(newer.RowId, older.RowId);
        var first = page.Items[0];
        first.Machine.Should().Be(_machine);
        first.Id.Should().Be(newer.Id);
        first.OwnerKind.Should().Be(SnapshotOwnerKind.User);
        first.State.Should().Be("Waiting");
        first.Version.Should().Be(3);
        first.CreatedAt.Should().Be(start.AddMinutes(4));
        first.UpdatedAt.Should().Be(start.AddMinutes(5));
        first.HasLiveInvokedRun.Should().BeTrue();
        page.Items[1].HasLiveInvokedRun.Should().BeFalse();
    }

    [Test]
    public void No_operator_read_carries_a_context_or_a_user_key()
    {
        var properties = typeof(MachineInstanceRecord)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToList();

        properties
            .Should()
            .NotContain(
                ["Context", "UserKey", "InvokeToken"],
                "operators see an instance's state, timestamps, owner kind and runs, not its data "
                    + "(central docs/0046), and no operator view names the user behind a row"
            );
    }

    [Test]
    public async Task Filters_by_state_and_owner_kind_and_pages()
    {
        var at = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < 5; i++)
            await Seed(SnapshotOwnerKind.System, "Running", at.AddSeconds(i));
        await Seed(SnapshotOwnerKind.User, "Running", at.AddSeconds(10));
        await Seed(SnapshotOwnerKind.System, "Done", at.AddSeconds(20));

        var running = await _operations.GetMachineInstancesAsync(
            new MachineInstanceQuery(
                _machine,
                "Running",
                SnapshotOwnerKind.System,
                Skip: 1,
                Take: 2
            ),
            CancellationToken.None
        );
        var all = await _operations.GetMachineInstancesAsync(
            new MachineInstanceQuery(_machine, "Running", SnapshotOwnerKind.System),
            CancellationToken.None
        );

        all.Items.Should().HaveCount(5);
        all.Items.Should()
            .OnlyContain(i => i.State == "Running" && i.OwnerKind == SnapshotOwnerKind.System);
        running
            .Items.Select(i => i.RowId)
            .Should()
            .Equal(all.Items.Skip(1).Take(2).Select(i => i.RowId));
        running.Skip.Should().Be(1);
        running.Take.Should().Be(2);

        (
            await _operations.CountMachineInstancesAsync(
                new MachineInstanceQuery(_machine, "Running"),
                CancellationToken.None
            )
        )
            .Should()
            .Be(new MachineInstanceTotal(6, Capped: false));
    }

    [Test]
    public async Task A_page_deeper_than_the_count_cap_is_refused_as_the_api_refuses_it()
    {
        await Seed(SnapshotOwnerKind.System, "Running", DateTimeOffset.UtcNow);

        var deepest = await _operations.GetMachineInstancesAsync(
            new MachineInstanceQuery(
                Machine: _machine,
                Skip: OperationsService.MachineInstanceCountCap
            ),
            CancellationToken.None
        );
        var tooDeep = () =>
            _operations.GetMachineInstancesAsync(
                new MachineInstanceQuery(
                    Machine: _machine,
                    Skip: OperationsService.MachineInstanceCountCap + 1
                ),
                CancellationToken.None
            );

        deepest.Items.Should().BeEmpty();
        deepest.Skip.Should().Be(OperationsService.MachineInstanceCountCap);
        await tooDeep
            .Should()
            .ThrowAsync<ArgumentOutOfRangeException>(
                "the API's machineInstances refuses a skip past it, and the dashboard reads the same call"
            );
    }

    [Test]
    public async Task The_state_counts_are_read_once_for_every_caller_within_a_few_seconds()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var operations = new OperationsService(
            Scope.ServiceProvider.GetRequiredService<Trax.Mediator.Services.TrainDiscovery.ITrainDiscoveryService>(),
            Scope.ServiceProvider.GetRequiredService<Trax.Effect.Data.Services.IDataContextFactory.IDataContextProviderFactory>(),
            new Trax.Scheduler.Configuration.SchedulerConfiguration(),
            Scope.ServiceProvider.GetRequiredService<Trax.Mediator.Services.TrainExecution.ITrainExecutionService>(),
            new ServiceCollection().AddSingleton<TimeProvider>(clock).BuildServiceProvider()
        );
        await Seed(SnapshotOwnerKind.System, "Running", DateTimeOffset.UtcNow);

        var first = await operations.GetMachineInstanceStateCountsAsync(
            _machine,
            CancellationToken.None
        );
        await Seed(SnapshotOwnerKind.System, "Running", DateTimeOffset.UtcNow);
        var cached = await operations.GetMachineInstanceStateCountsAsync(
            _machine,
            CancellationToken.None
        );
        clock.Advance(OperationsService.MachineInstanceCountCacheDuration);
        var fresh = await operations.GetMachineInstanceStateCountsAsync(
            _machine,
            CancellationToken.None
        );

        first.Single().Count.Should().Be(1);
        cached.Should().Equal(first, "a second poll within the window reads no table");
        fresh
            .Single()
            .Count.Should()
            .Be(2, "once the window has passed, the counts are read again");
    }

    private sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    [Test]
    public async Task The_count_stops_at_its_cap()
    {
        await ((DbContext)DataContext).Database.ExecuteSqlInterpolatedAsync(
            $$"""
            INSERT INTO trax.snapshot_draft (id, user_key, owner_kind, machine, version, state, context, concurrency_token, updated_at)
            SELECT gen_random_uuid(), NULL, 'system', {{_machine}}, 1, 'Bulk', '{}', gen_random_uuid(), now()
            FROM generate_series(1, {{OperationsService.MachineInstanceCountCap + 1}})
            """
        );

        var total = await _operations.CountMachineInstancesAsync(
            new MachineInstanceQuery(Machine: _machine),
            CancellationToken.None
        );

        total
            .Should()
            .Be(new MachineInstanceTotal(OperationsService.MachineInstanceCountCap, true));
    }

    [Test]
    public async Task A_lookup_names_the_owner_kind_so_a_users_draft_never_answers_for_a_system_instance()
    {
        var id = Guid.NewGuid();
        var at = DateTimeOffset.UtcNow;
        var system = await Seed(SnapshotOwnerKind.System, "Running", at, id: id);
        var user = await Seed(SnapshotOwnerKind.User, "Draft", at, id: id);

        var asSystem = await _operations.GetMachineInstanceAsync(
            new MachineInstanceKey(_machine, SnapshotOwnerKind.System, id),
            CancellationToken.None
        );
        var asUser = await _operations.GetMachineInstanceAsync(
            new MachineInstanceKey(_machine, SnapshotOwnerKind.User, id, user.RowId),
            CancellationToken.None
        );
        var crossed = await _operations.GetMachineInstanceAsync(
            new MachineInstanceKey(_machine, SnapshotOwnerKind.User, id, system.RowId),
            CancellationToken.None
        );

        asSystem!.RowId.Should().Be(system.RowId);
        asSystem.State.Should().Be("Running");
        asUser!.RowId.Should().Be(user.RowId);
        asUser.State.Should().Be("Draft");
        crossed.Should().BeNull("the system's row is not a user's draft, whatever its row id");
    }

    [Test]
    public async Task A_users_draft_is_not_looked_up_without_its_row_id()
    {
        var act = () =>
            _operations.GetMachineInstanceAsync(
                new MachineInstanceKey(_machine, SnapshotOwnerKind.User, Guid.NewGuid()),
                CancellationToken.None
            );

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*row id*");
    }

    [Test]
    public async Task Counts_each_state_per_machine_and_owner_kind()
    {
        var at = DateTimeOffset.UtcNow;
        await Seed(SnapshotOwnerKind.System, "Running", at);
        await Seed(SnapshotOwnerKind.System, "Running", at);
        await Seed(SnapshotOwnerKind.User, "Running", at);
        await Seed(SnapshotOwnerKind.System, "Done", at);

        var counts = await _operations.GetMachineInstanceStateCountsAsync(
            _machine,
            CancellationToken.None
        );

        counts
            .Should()
            .Equal(
                new MachineInstanceStateCount(_machine, "Done", SnapshotOwnerKind.System, 1),
                new MachineInstanceStateCount(_machine, "Running", SnapshotOwnerKind.User, 1),
                new MachineInstanceStateCount(_machine, "Running", SnapshotOwnerKind.System, 2)
            );
    }

    private async Task<SnapshotDraft> Seed(
        SnapshotOwnerKind owner,
        string state,
        DateTimeOffset updatedAt,
        Guid? id = null,
        string? invokeToken = null
    )
    {
        var row = new SnapshotDraft
        {
            Id = id ?? Guid.NewGuid(),
            OwnerKind = owner,
            UserKey = owner == SnapshotOwnerKind.User ? $"user-{Guid.NewGuid():N}" : null,
            Machine = _machine,
            Version = 3,
            State = state,
            Context = SecretContext,
            ConcurrencyToken = Guid.NewGuid(),
            CreatedAt = updatedAt.AddMinutes(-1),
            UpdatedAt = updatedAt,
            InvokeToken = invokeToken,
        };
        DataContext.SnapshotDrafts.Add(row);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();
        return row;
    }
}
