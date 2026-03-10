using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Extensions;
using Trax.Mediator.Configuration;
using Trax.Mediator.Extensions;
using Trax.Mediator.Services.TrainBus;
using Trax.Mediator.Services.TrainRegistry;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.UnitTests;

[TestFixture]
public class MediatorServiceExtensionsTests
{
    #region Existing Registration Tests

    [Test]
    public void AddServiceTrainBus_RegistersITrainBus()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects)
                .AddMediator(assemblies: [typeof(AssemblyMarker).Assembly])
        );
        using var provider = services.BuildServiceProvider();

        // Act
        var trainBus = provider.GetService<ITrainBus>();

        // Assert
        trainBus.Should().NotBeNull();
    }

    [Test]
    public void AddServiceTrainBus_RegistersITrainRegistry()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects)
                .AddMediator(assemblies: [typeof(AssemblyMarker).Assembly])
        );
        using var provider = services.BuildServiceProvider();

        // Act
        var registry = provider.GetService<ITrainRegistry>();

        // Assert
        registry.Should().NotBeNull();
    }

    [Test]
    public void RegisterServiceTrains_WithNoAssemblies_DoesNotThrow()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act & Assert
        var act = () => services.RegisterServiceTrains();
        act.Should().NotThrow();
    }

    #endregion

    #region Func<> Lambda Overload

    [Test]
    public void AddMediator_WithFuncLambda_RegistersTrainBus()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects)
                .AddMediator(mediator => mediator.ScanAssemblies(typeof(AssemblyMarker).Assembly))
        );
        using var provider = services.BuildServiceProvider();

        provider.GetService<ITrainBus>().Should().NotBeNull();
        provider.GetService<ITrainRegistry>().Should().NotBeNull();
    }

    #endregion

    #region MediatorConfiguration Singleton

    [Test]
    public void MediatorConfiguration_RegisteredAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects)
                .AddMediator(assemblies: [typeof(AssemblyMarker).Assembly])
        );
        using var provider = services.BuildServiceProvider();

        var config = provider.GetService<MediatorConfiguration>();
        config.Should().NotBeNull();
        config!.Assemblies.Should().HaveCount(1);
        config.TrainLifetime.Should().Be(ServiceLifetime.Transient);
    }

    [Test]
    public void MediatorConfiguration_ReflectsCustomLifetime()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects)
                .AddMediator(mediator =>
                    mediator
                        .ScanAssemblies(typeof(AssemblyMarker).Assembly)
                        .TrainLifetime(ServiceLifetime.Scoped)
                )
        );
        using var provider = services.BuildServiceProvider();

        var config = provider.GetRequiredService<MediatorConfiguration>();
        config.TrainLifetime.Should().Be(ServiceLifetime.Scoped);
    }

    #endregion

    #region Build-Time Validation

    [Test]
    public void AddMediator_WithoutAssemblies_ThrowsWithHelpfulMessage()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var act = () =>
            services.AddTrax(trax =>
                trax.AddEffects(effects => effects).AddMediator(mediator => mediator)
            );

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*AddMediator()*")
            .WithMessage("*ScanAssemblies()*");
    }

    [Test]
    public void AddMediator_ErrorMessage_ContainsCodeExample()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var act = () =>
            services.AddTrax(trax =>
                trax.AddEffects(effects => effects).AddMediator(mediator => mediator)
            );

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*services.AddTrax*")
            .WithMessage("*.AddEffects*")
            .WithMessage("*.AddMediator*")
            .WithMessage("*.ScanAssemblies(typeof(Program).Assembly)*");
    }

    #endregion
}
