using System.Reflection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.StateMachine.Persistence.Integration.Fakes;
using Trax.Effect.StateMachine.Persistence.Mutations;

namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary>
/// The <c>trax.AddStateMachines(...)</c> builder step (Change 3): one call discovers the machines, wires the
/// subsystem, auto-registers the <see cref="SnapshotDbContext"/> against the configured provider, and
/// contributes the generic mutations to the mediator scan, none of which the host does by hand. Plus its
/// ordering guard.
/// </summary>
[TestFixture]
public class AddStateMachinesBuilderTests
{
    [Test]
    public void AddStateMachines_wires_the_subsystem_auto_registers_the_context_and_contributes_the_mutations()
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

        // The SnapshotDbContext is auto-registered against the Postgres provider (no host AddDbContext), so
        // resolving it runs the provider's configurator and it round-trips a record.
        var db = sp.GetRequiredService<SnapshotDbContext>();
        db.Database.ProviderName.Should().Contain("Npgsql");

        var id = Guid.NewGuid();
        db.SnapshotDrafts.Add(
            new SnapshotRecord
            {
                Id = id,
                UserKey = "u1",
                Machine = "order",
                Version = 1,
                State = "Cart",
                Context = "{}",
                ConcurrencyToken = Guid.NewGuid(),
                UpdatedAt = DateTimeOffset.UtcNow,
            }
        );
        db.SaveChanges();

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
