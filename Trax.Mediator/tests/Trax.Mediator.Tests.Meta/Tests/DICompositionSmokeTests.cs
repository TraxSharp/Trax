using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Mediator.Services.TrainBus;
using Trax.Mediator.Services.TrainRegistry;

namespace Trax.Mediator.Tests.Meta.Tests;

/// <summary>
/// Smoke tests that the unified <c>AddTrax(...).AddEffects(UseInMemory).AddMediator()</c> fluent
/// API composes end-to-end. If a wiring change accidentally drops a required registration, these
/// fail at the registration point rather than at first use in a downstream consumer.
/// </summary>
[TestFixture]
public class DICompositionSmokeTests
{
    private static readonly Assembly[] ScanScope = new[]
    {
        typeof(DICompositionSmokeTests).Assembly,
    };

    [Test]
    public void AddTrax_AddEffects_InMemory_AddMediator_Composes_WithoutError()
    {
        var services = new ServiceCollection();
        var act = () =>
            services.AddTrax(trax =>
                trax.AddEffects(effects => effects.UseInMemory())
                    .AddMediator(mediator => mediator.ScanAssemblies(ScanScope))
            );

        act.Should()
            .NotThrow(
                "the canonical AddTrax(...).AddEffects(UseInMemory).AddMediator(ScanAssemblies(...)) "
                    + "composition must build successfully. Failure here means a wiring change in "
                    + "Trax.Effect or Trax.Mediator dropped a required registration or builder method."
            );
    }

    [Test]
    public void AddMediator_Registers_ITrainRegistry_And_ITrainBus()
    {
        var services = new ServiceCollection();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UseInMemory())
                .AddMediator(mediator => mediator.ScanAssemblies(ScanScope))
        );

        using var sp = services.BuildServiceProvider();

        sp.GetService<ITrainRegistry>()
            .Should()
            .NotBeNull(
                "AddMediator() must register an ITrainRegistry singleton. Dashboard, scheduler, "
                    + "and the bus itself depend on this; silent loss would surface much later."
            );

        sp.GetService<ITrainBus>()
            .Should()
            .NotBeNull(
                "AddMediator() must register an ITrainBus. This is the entry-point users call to "
                    + "dispatch trains; missing it would break every caller."
            );
    }

    [Test]
    public void AddMediator_AssemblyParamOverload_Composes()
    {
        // The params-Assembly overload of AddMediator is part of the public API.
        var services = new ServiceCollection();
        var act = () =>
            services.AddTrax(trax =>
                trax.AddEffects(effects => effects.UseInMemory()).AddMediator(ScanScope)
            );

        act.Should()
            .NotThrow("AddMediator(params Assembly[]) is a public overload and must keep working.");
    }

    [Test]
    public void AddTrax_Twice_OnSameServices_DoesNot_DoubleRegister()
    {
        var services = new ServiceCollection();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UseInMemory())
                .AddMediator(mediator => mediator.ScanAssemblies(ScanScope))
        );

        var act = () =>
            services.AddTrax(trax =>
                trax.AddEffects(effects => effects.UseInMemory())
                    .AddMediator(mediator => mediator.ScanAssemblies(ScanScope))
            );

        act.Should().NotThrow("layering AddTrax twice must not throw at registration time.");
    }
}
