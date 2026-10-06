using System.Reflection;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Extensions;
using Trax.Effect.StateMachine.Persistence.Integration.Fakes;
using Trax.Effect.StateMachine.Persistence.Mutations;

namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary>
/// The <c>trax.AddStateMachines(...)</c> builder step: one call discovers the machines, wires the subsystem
/// over the data context the configured provider registers, and contributes the generic mutations to the
/// mediator scan, none of which the host does by hand. Plus its ordering guard.
/// </summary>
[TestFixture]
public class AddStateMachinesBuilderTests
{
    [Test]
    public async Task AddStateMachines_wires_the_subsystem_over_the_data_context_and_contributes_the_mutations()
    {
        var services = new ServiceCollection();
        services.AddScoped<ISnapshotPrincipal>(_ => new FakePrincipal("u1"));
        services.AddScoped<IOrderCharge, CountingEffect>();

        IReadOnlyList<Assembly> contributed = [];
        services.AddTrax(trax =>
        {
            var effects = trax.AddEffects(e => e.UsePostgres(PostgresSetup.ConnectionString))
                .AddStateMachines(typeof(OrderMachine).Assembly);
            // The generic mutations' assembly is contributed to the mediator scan — the host never names it.
            contributed = effects.Root.ContributedMediatorAssemblies.ToList();
        });

        contributed.Should().Contain(StateMachineMutations.Assembly);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;

        // The whole subsystem resolves: the registry discovered the machines, and the store is wired.
        sp.GetRequiredService<ISnapshotMachineRegistry>().Service("order").Should().NotBeNull();
        sp.GetRequiredService<ISnapshotStore>().Should().NotBeNull();

        // The store reaches snapshot_draft through the provider's data context: a draft it writes is read
        // back through IDataContext.SnapshotDrafts, with no DbContext of the subsystem's own.
        var id = Guid.NewGuid();
        (
            await sp.GetRequiredService<ISnapshotMachineRegistry>()
                .Service("order")!
                .Autosave("u1", id, OrderMachine.ReviewSnapshot(1))
        )
            .Should()
            .BeOfType<AutosaveResult.Saved>();

        var db = sp.GetRequiredService<IDataContext>();
        ((DbContext)db).Database.ProviderName.Should().Contain("Npgsql");
        db.SnapshotDrafts.AsNoTracking()
            .Single(x => x.Id == id && x.UserKey == "u1")
            .Machine.Should()
            .Be("order");
    }

    [Test]
    public void AddStateMachines_after_AddMediator_throws()
    {
        var services = new ServiceCollection();

        var act = () =>
            services.AddTrax(trax =>
            {
                var effects = trax.AddEffects(e => e.UseInMemory());
                // Simulate AddMediator having already built its route registry.
                effects.Root.MediatorConfigured = true;
                effects.AddStateMachines(typeof(OrderMachine).Assembly);
            });

        act.Should().Throw<InvalidOperationException>().WithMessage("*before AddMediator*");
    }
}
