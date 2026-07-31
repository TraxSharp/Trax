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
    public void AddTraxStateMachines_with_no_machines_throws_a_helpful_error()
    {
        var services = new ServiceCollection();

        var act = () => services.AddTraxStateMachines(typeof(string).Assembly);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*AddTraxStateMachines*no machines*");
    }
}
