using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Configuration;
using Trax.Mediator.Extensions;
using Trax.Mediator.Services.TrainBus;
using Trax.Mediator.Services.TrainRegistry;
using Trax.Mediator.Tests.MemoryLeak.Integration.Fakes.Models;
using Trax.Mediator.Tests.MemoryLeak.Integration.Fakes.Trains;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.UnitTests;

[TestFixture]
public class MediatorServiceExtensionsTests
{
    #region Existing Registration Tests

    /// <summary>
    /// A singleton train may inject the enqueue services. <c>TrainLifetime(Singleton)</c> is the
    /// supported route to such a train, and <c>ValidateScopes</c> and <c>ValidateOnBuild</c> are
    /// both on by default in Development for <c>WebApplication</c> — so registering those
    /// services scoped fails the host at startup rather than leaving a latent risk.
    ///
    /// <para>The train is registered directly rather than scanned, so what is asserted is the
    /// lifetime of the two enqueue services and not the dependencies of every train in this
    /// assembly; several are deliberately unresolvable.</para>
    /// </summary>
    [Test]
    public void AddMediator_WhenASingletonTrainInjectsTheEnqueueContext_PassesScopeValidation()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UseInMemory())
                .AddMediator(mediator =>
                    mediator
                        .TrainLifetime(ServiceLifetime.Singleton)
                        // Trax.Mediator itself declares no trains, so the scan contributes none
                        // and the container holds exactly the train registered below.
                        .ScanAssemblies(typeof(ITrainBus).Assembly)
                )
        );
        services.AddSingleton<IEnqueueContextConsumingTrain, EnqueueContextConsumingTrain>();

        // Act
        var build = () =>
            services.BuildServiceProvider(
                new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
            );

        // Assert
        build
            .Should()
            .NotThrow(
                "neither enqueue service holds per-scope state, so a singleton train consuming "
                    + "one is a supported shape"
            );
    }

    /// <summary>
    /// The startup gates run before a hosted service the host registered before calling AddMediator.
    /// </summary>
    /// <remarks>
    /// Hosted services start in registration order. A worker registered first began claiming and
    /// running work before the chain check had refused the host, and under
    /// <c>ServicesStartConcurrently</c> it ran alongside it, so the host was disposed out from under
    /// those runs and their rows were left <c>InProgress</c> holding their subjects.
    /// </remarks>
    [Test]
    public void AddMediator_StartupGates_AreOrderedBeforeAHostedServiceRegisteredFirst()
    {
        // Arrange: the host's own worker goes in before Trax is configured at all.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHostedService<MarkerWorker>();

        // Act
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UseInMemory())
                .AddMediator(mediator => mediator.ScanAssemblies(typeof(ITrainBus).Assembly))
        );

        // Assert
        var hosted = services
            .Select((descriptor, index) => (descriptor, index))
            .Where(entry => entry.descriptor.ServiceType == typeof(IHostedService))
            .ToList();

        var marker = hosted.Single(e => e.descriptor.ImplementationType == typeof(MarkerWorker));
        var chainCheck = hosted.Single(e =>
            e.descriptor.ImplementationType?.Name == "TrainChainStartupValidator"
        );

        chainCheck
            .index.Should()
            .BeLessThan(
                marker.index,
                "a gate that only sometimes runs before the work it gates is not a gate"
            );
    }

    private sealed class MarkerWorker : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

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

    #region Deduplication (pre-scanned registration)

    [Test]
    public void AddServiceTrainBus_WithPreScannedTypes_RegistersAllTrains()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects)
                .AddMediator(assemblies: [typeof(AssemblyMarker).Assembly])
        );
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetService<IMemoryTestTrain>().Should().NotBeNull();
        scope.ServiceProvider.GetService<IFailingTestTrain>().Should().NotBeNull();
        scope.ServiceProvider.GetService<INestedTestTrain>().Should().NotBeNull();
    }

    [Test]
    public void AddServiceTrainBus_WithPreScannedTypes_SetsCanonicalName()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects)
                .AddMediator(assemblies: [typeof(AssemblyMarker).Assembly])
        );
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var train = scope.ServiceProvider.GetRequiredService<IMemoryTestTrain>();
        var serviceTrain =
            train as ServiceTrain<Fakes.Models.MemoryTestInput, Fakes.Models.MemoryTestOutput>;
        serviceTrain.Should().NotBeNull();
        serviceTrain!.CanonicalName.Should().Be(typeof(IMemoryTestTrain).FullName);
    }

    [Test]
    public void AddServiceTrainBus_WithPreScannedTypes_TrainRegistryConsistent()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects)
                .AddMediator(assemblies: [typeof(AssemblyMarker).Assembly])
        );
        using var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<ITrainRegistry>();
        // InputTypeToTrain should contain entries for our test trains
        registry.InputTypeToTrain.Should().NotBeEmpty();
        registry.InputTypeToTrain.Values.Should().Contain(typeof(IMemoryTestTrain));
    }

    [Test]
    public void AddServiceTrainBus_WithScopedLifetime_RegistersAllTrainsAsScoped()
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
        using var scope = provider.CreateScope();

        // All trains should be resolvable from a scope
        scope.ServiceProvider.GetService<IMemoryTestTrain>().Should().NotBeNull();
        scope.ServiceProvider.GetService<IFailingTestTrain>().Should().NotBeNull();
        scope.ServiceProvider.GetService<INestedTestTrain>().Should().NotBeNull();
    }

    #endregion

    #region Trains Without Dedicated Interface

    [Test]
    public void InterfacelessTrain_IsRegisteredInDI()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects)
                .AddMediator(assemblies: [typeof(AssemblyMarker).Assembly])
        );
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var train = scope.ServiceProvider.GetService<
            IServiceTrain<InterfacelessTestInput, InterfacelessTestOutput>
        >();

        train.Should().NotBeNull();
        train.Should().BeOfType<InterfacelessTestTrain>();
    }

    [Test]
    public void InterfacelessTrain_SetsCanonicalName()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects)
                .AddMediator(assemblies: [typeof(AssemblyMarker).Assembly])
        );
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var train = scope.ServiceProvider.GetRequiredService<
            IServiceTrain<InterfacelessTestInput, InterfacelessTestOutput>
        >();

        var serviceTrain = train as ServiceTrain<InterfacelessTestInput, InterfacelessTestOutput>;
        serviceTrain.Should().NotBeNull();
        serviceTrain!
            .CanonicalName.Should()
            .Be(typeof(IServiceTrain<InterfacelessTestInput, InterfacelessTestOutput>).FullName);
    }

    [Test]
    public async Task InterfacelessTrain_ProviderDisposal_DoesNotHang()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects)
                .AddMediator(assemblies: [typeof(AssemblyMarker).Assembly])
        );
        var provider = services.BuildServiceProvider();

        // Resolve the interfaceless train so DI tracks it
        using (var scope = provider.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<
                IServiceTrain<InterfacelessTestInput, InterfacelessTestOutput>
            >();
        }

        // Disposal should complete without hanging (the bug caused infinite hang here)
        var disposeTask = provider.DisposeAsync();
        var completed = await Task.WhenAny(
            disposeTask.AsTask(),
            Task.Delay(TimeSpan.FromSeconds(10))
        );

        completed.Should().Be(disposeTask.AsTask(), "ServiceProvider disposal should not hang");
    }

    [Test]
    public void InterfacelessTrain_InInputTypeToTrain_IsResolvableFromDI()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects)
                .AddMediator(assemblies: [typeof(AssemblyMarker).Assembly])
        );
        using var provider = services.BuildServiceProvider();

        // Get the service type from the registry
        var registry = provider.GetRequiredService<ITrainRegistry>();
        registry.InputTypeToTrain.Should().ContainKey(typeof(InterfacelessTestInput));

        var serviceType = registry.InputTypeToTrain[typeof(InterfacelessTestInput)];

        // That service type should be resolvable from DI
        using var scope = provider.CreateScope();
        var resolved = scope.ServiceProvider.GetService(serviceType);
        resolved.Should().NotBeNull();
        resolved.Should().BeOfType<InterfacelessTestTrain>();
    }

    #endregion
}
