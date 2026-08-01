using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.StateMachine.Persistence.Integration.Fakes;
using Trax.Effect.StateMachine.Persistence.Integration.Fixtures;

namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary>
/// The one-line registration: AddTraxStateMachines(assembly) discovers every fluent machine and resolves
/// the whole subsystem through real DI, no per-machine registration, no effect wiring in the container.
/// </summary>
public class AddTraxStateMachinesTests
{
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddDbContext<SnapshotDbContext>(o => o.UseNpgsql(PostgresSetup.ConnectionString));
        services.AddScoped<ISnapshotPrincipal>(_ => new FakePrincipal("u"));
        services.AddScoped<IOrderCharge, CountingEffect>();

        services.AddTraxStateMachines(typeof(OrderMachine).Assembly);

        return services.BuildServiceProvider();
    }

    [Test]
    public void AddTraxStateMachines_discovers_machines_and_resolves_the_registry_and_services()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<ISnapshotMachineRegistry>();

        registry.Service("turnstile").Should().NotBeNull();
        registry.Service("order").Should().NotBeNull();
        registry.Service("does-not-exist").Should().BeNull();

        // The order machine's inline effect is discovered and its runner resolves; the turnstile has none.
        registry.EffectRunner("order").Should().NotBeNull();
        registry.EffectRunner("turnstile").Should().BeNull();
    }

    [Test]
    public async Task AddTraxStateMachines_honors_a_configured_draft_ttl()
    {
        var services = new ServiceCollection();
        services.AddDbContext<SnapshotDbContext>(o => o.UseNpgsql(PostgresSetup.ConnectionString));
        services.AddScoped<ISnapshotPrincipal>(_ => new FakePrincipal("u"));
        services.AddScoped<IOrderCharge, CountingEffect>();
        services.AddTraxStateMachines(
            o => o.DraftTtl = TimeSpan.FromMinutes(1),
            typeof(OrderMachine).Assembly
        );
        using var provider = services.BuildServiceProvider();

        provider
            .GetRequiredService<StateMachineOptions>()
            .DraftTtl.Should()
            .Be(TimeSpan.FromMinutes(1));

        // The registry-built service actually applies the TTL: a draft aged past it expires on the next load.
        var id = Guid.NewGuid();
        using (var scope = provider.CreateScope())
        {
            var svc = scope
                .ServiceProvider.GetRequiredService<ISnapshotMachineRegistry>()
                .Service("turnstile")!;
            (await svc.Autosave("u", id, TestTurnstile.UnlockedJson))
                .Should()
                .BeOfType<AutosaveResult.Saved>();
        }
        await TestDb.BackdateDraft("u", id, DateTimeOffset.UtcNow.AddHours(-1));
        using (var scope = provider.CreateScope())
        {
            var svc = scope
                .ServiceProvider.GetRequiredService<ISnapshotMachineRegistry>()
                .Service("turnstile")!;
            (await svc.Load("u", id)).Should().BeOfType<LoadResult.NotFound>();
        }
    }

    [Test]
    public void AddTraxStateMachines_with_no_machines_throws_a_helpful_error()
    {
        var services = new ServiceCollection();

        var act = () => services.AddTraxStateMachines(typeof(string).Assembly);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*AddTraxStateMachines*no machines*");
    }
}
