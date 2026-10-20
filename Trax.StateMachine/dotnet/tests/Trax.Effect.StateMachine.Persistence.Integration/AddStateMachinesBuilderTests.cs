using System.Reflection;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Attributes;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Extensions;
using Trax.Effect.StateMachine.Persistence.Integration.Fakes;
using Trax.Effect.StateMachine.Persistence.Integration.Fixtures;
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
                .Service("turnstile")!
                .Autosave("u1", id, TestTurnstile.InitialJson)
        )
            .Should()
            .BeOfType<AutosaveResult.Saved>();

        var db = sp.GetRequiredService<IDataContext>();
        ((DbContext)db).Database.ProviderName.Should().Contain("Npgsql");
        db.SnapshotDrafts.AsNoTracking()
            .Single(x => x.Id == id && x.UserKey == "u1")
            .Machine.Should()
            .Be("turnstile");
    }

    [Test]
    [Property(
        "adr",
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md"
    )]
    public async Task Without_the_opt_in_the_internal_trains_route_but_are_not_exposed()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<ISnapshotPrincipal>(_ => new FakePrincipal("u1"));
        services.AddScoped<IOrderCharge, CountingEffect>();
        services.AddTrax(trax =>
            trax.AddEffects(e => e.UsePostgres(PostgresSetup.ConnectionString))
                .AddStateMachines(typeof(OrderMachine).Assembly)
        );

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var advance = scope.ServiceProvider.GetRequiredService<IAdvanceSnapshot>();
        var send = scope.ServiceProvider.GetRequiredService<ISendSnapshot>();

        // The per-machine surface dispatches these two trains, so they stay routable; nothing serves them to a
        // client unless the host opts in to the generic mutations.
        new[] { advance.GetType(), send.GetType() }
            .Where(type => type.GetCustomAttribute<TraxMutationAttribute>() is not null)
            .Should()
            .BeEmpty(
                "without the opt-in a route carries no GraphQL exposure. See "
                    + "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md"
            );
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

    [Test]
    public void A_second_AddStateMachines_call_is_refused_rather_than_replacing_the_first()
    {
        var services = new ServiceCollection();

        var act = () =>
            services.AddTrax(trax =>
                trax.AddEffects(e => e.UseInMemory())
                    .AddStateMachines(
                        o => o.DraftTtl = TimeSpan.FromDays(1),
                        typeof(OrderMachine).Assembly
                    )
                    .AddStateMachines(typeof(OrderMachine).Assembly)
            );

        act.Should()
            .Throw<InvalidOperationException>(
                "a second call would replace the first one's options, so what it served would silently change"
            )
            .WithMessage("*AddStateMachines() was called more than once*");
    }

    [Test]
    [Property(
        "adr",
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md"
    )]
    public void A_machine_the_host_registered_before_the_scan_finds_it_is_registered_once()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMachine, OrderMachine>();

        services.AddTrax(trax =>
            trax.AddEffects(e => e.UseInMemory()).AddStateMachines(typeof(OrderMachine).Assembly)
        );

        services
            .Count(d =>
                d.ServiceType == typeof(IMachine) && d.ImplementationType == typeof(OrderMachine)
            )
            .Should()
            .Be(1, "the scan adds a machine only when its type is not registered already");
    }

    [Test]
    [Property(
        "adr",
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md"
    )]
    public void The_same_machine_type_registered_twice_is_one_machine_to_the_registry()
    {
        var services = new ServiceCollection();
        services.AddScoped<ISnapshotPrincipal>(_ => new FakePrincipal("u1"));
        services.AddScoped<IOrderCharge, CountingEffect>();
        services.AddTrax(trax =>
            trax.AddEffects(e => e.UseInMemory()).AddStateMachines(typeof(OrderMachine).Assembly)
        );
        services.AddSingleton<IMachine>(new OrderMachine());

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope
            .ServiceProvider.GetRequiredService<ISnapshotMachineRegistry>()
            .Service("order")
            .Should()
            .NotBeNull(
                "a second registration of one machine type is the same machine, not a clash"
            );
    }

    [Test]
    public async Task A_host_that_registers_no_machine_refuses_to_start()
    {
        var refusal = await StartingHost.Start(_ => { });

        refusal
            .Should()
            .BeOfType<InvalidOperationException>(
                "a host that forgot to pass its machines' assemblies, and registers none itself, is told so"
            )
            .Which.Message.Should()
            .Contain("No state machines are registered");
    }
}
