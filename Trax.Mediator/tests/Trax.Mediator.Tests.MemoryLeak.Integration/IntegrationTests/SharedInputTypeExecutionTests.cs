using FluentAssertions;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Attributes;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Exceptions;
using Trax.Mediator.Extensions;
using Trax.Mediator.Services.RunExecutor;
using Trax.Mediator.Services.TrainBus;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.IntegrationTests;

/// <summary>
/// Two trains may take the same input type. <see cref="ITrainBus"/>'s input-keyed
/// <c>RunAsync</c> can pick only one of them, so everything that names a train (discovery, a run
/// by name, the executor) works per train: it runs exactly the train it found and authorized.
///
/// <para>Enforces <c>docs/adr/0005-a-train-run-by-name-is-the-train-that-runs.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0005-a-train-run-by-name-is-the-train-that-runs.md")]
[TestFixture]
public class SharedInputTypeExecutionTests
{
    private const string Adr = "docs/adr/0005-a-train-run-by-name-is-the-train-that-runs.md";

    private ServiceProvider _serviceProvider = null!;

    [SetUp]
    public void Setup()
    {
        TrainBus.ClearMethodCache();

        // The open train is registered by hand before the scan, the way a host adds one route
        // itself. The scan also finds the gated train, declared first below, so the bus's
        // input-type entry points at the gated one.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScopedTraxRoute<IOpenSharedTrain, OpenSharedTrain>();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UseInMemory())
                .AddMediator(assemblies: [typeof(SharedInputTypeExecutionTests).Assembly])
        );

        _serviceProvider = services.BuildServiceProvider();
    }

    [TearDown]
    public async Task TearDown()
    {
        await _serviceProvider.DisposeAsync();
        TrainBus.ClearMethodCache();
    }

    [Test]
    public async Task RunAsync_ByName_RunsTheNamedTrain_NotTheOneRegisteredForItsInputType()
    {
        using var scope = _serviceProvider.CreateScope();
        var execution = scope.ServiceProvider.GetRequiredService<ITrainExecutionService>();
        var bus = scope.ServiceProvider.GetRequiredService<ITrainBus>();
        bus.InitializeTrain(new SharedInput())
            .Should()
            .BeOfType<GatedSharedTrain>("the fixture's input-type entry must be the other train");

        var result = await execution.RunAsync(
            typeof(IOpenSharedTrain).FullName!,
            """{"value":"x"}"""
        );

        result
            .Output.Should()
            .Be(new SharedOutput("open"), $"a train run by name is the train that runs ({Adr})");
    }

    [Test]
    public async Task RunAsync_ByName_TheGatedTrain_IsHeldToItsOwnRequirements()
    {
        using var scope = _serviceProvider.CreateScope();
        var execution = scope.ServiceProvider.GetRequiredService<ITrainExecutionService>();

        var act = () =>
            execution.RunAsync(typeof(IGatedSharedTrain).FullName!, """{"value":"x"}""");

        await act.Should()
            .ThrowAsync<TrainAuthorizationNotConfiguredException>(
                $"the named train's own requirements apply ({Adr})"
            );
    }

    [Test]
    public void Discovery_TwoTrainsRegisteredUnderOneBaseClass_IsRefusedNamingBoth()
    {
        var services = new ServiceCollection();
        services.AddTransient<
            UnitTests.TrainServiceTypeSelectionTests.AuditedTrainBase<
                UnitTests.TrainServiceTypeSelectionTests.MarkedInput,
                Unit
            >,
            UnitTests.TrainServiceTypeSelectionTests.MarkedTrain
        >();
        services.AddTransient<
            UnitTests.TrainServiceTypeSelectionTests.AuditedTrainBase<
                UnitTests.TrainServiceTypeSelectionTests.MarkedInput,
                Unit
            >,
            UnitTests.TrainServiceTypeSelectionTests.SecondMarkedTrain
        >();

        var discover = () => new TrainDiscoveryService(services).DiscoverTrains();

        discover
            .Should()
            .Throw<Trax.Core.Exceptions.TrainException>(
                "both would be listed under one name while the container runs only the last, so "
                    + $"the train a name describes would not be the train that runs ({Adr})"
            )
            .Which.Message.Should()
            .Contain("2 trains are registered under")
            .And.Contain(typeof(UnitTests.TrainServiceTypeSelectionTests.MarkedTrain).FullName!)
            .And.Contain(
                typeof(UnitTests.TrainServiceTypeSelectionTests.SecondMarkedTrain).FullName!
            );
    }

    [Test]
    public async Task LocalRunExecutor_RunsTheTrainItIsNamed()
    {
        using var scope = _serviceProvider.CreateScope();
        var executor = scope.ServiceProvider.GetRequiredService<IRunExecutor>();

        var open = await executor.ExecuteAsync(
            typeof(IOpenSharedTrain).FullName!,
            new SharedInput { Value = "x" },
            typeof(SharedOutput)
        );
        var gated = await executor.ExecuteAsync(
            typeof(IGatedSharedTrain).FullName!,
            new SharedInput { Value = "x" },
            typeof(SharedOutput)
        );

        open.Output.Should().Be(new SharedOutput("open"), $"the executor runs by name ({Adr})");
        gated.Output.Should().Be(new SharedOutput("gated"), $"the executor runs by name ({Adr})");
    }

    [Test]
    public async Task TrainBus_RunByName_RunsTheNamedTrainAsThePendingMetadata()
    {
        using var scope = _serviceProvider.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<ITrainBus>();
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(IOpenSharedTrain).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
            }
        );

        var output = await bus.RunByNameAsync<SharedOutput>(
            typeof(IOpenSharedTrain).FullName!,
            new SharedInput { Value = "x" },
            CancellationToken.None,
            metadata
        );

        output.Should().Be(new SharedOutput("open"));
        metadata.TrainState.Should().Be(TrainState.Completed);
    }

    [Test]
    public async Task TrainBus_RunByName_WithoutOutput_RunsTheNamedTrain()
    {
        using var scope = _serviceProvider.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<ITrainBus>();
        OpenSharedTrain.Runs = 0;

        await bus.RunByNameAsync(
            typeof(IOpenSharedTrain).FullName!,
            new SharedInput { Value = "x" },
            CancellationToken.None
        );

        OpenSharedTrain.Runs.Should().Be(1);
    }

    [Test]
    public async Task TrainBus_RunByName_UnknownName_IsRefused()
    {
        using var scope = _serviceProvider.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<ITrainBus>();

        var act = () =>
            bus.RunByNameAsync("No.Such.Train", new SharedInput(), CancellationToken.None);

        await act.Should().ThrowAsync<Trax.Core.Exceptions.TrainException>();
    }

    [Test]
    public async Task TrainBus_RunByName_InputOfAnotherType_IsRefusedBeforeTheTrainRuns()
    {
        using var scope = _serviceProvider.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<ITrainBus>();

        var act = () =>
            bus.RunByNameAsync(
                typeof(IOpenSharedTrain).FullName!,
                "not a SharedInput",
                CancellationToken.None
            );

        await act.Should()
            .ThrowAsync<Trax.Core.Exceptions.TrainException>()
            .WithMessage($"*{nameof(SharedInput)}*");
    }

    [Test]
    public async Task TrainBus_RunByName_NullInput_IsRefused()
    {
        using var scope = _serviceProvider.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<ITrainBus>();

        var act = () =>
            bus.RunByNameAsync(typeof(IOpenSharedTrain).FullName!, null!, CancellationToken.None);

        await act.Should()
            .ThrowAsync<Trax.Core.Exceptions.TrainException>()
            .WithMessage("trainInput is null*");
    }

    [Test]
    public async Task TrainBus_RunByName_MetadataThatIsNotPending_IsRefusedBeforeTheTrainRuns()
    {
        using var scope = _serviceProvider.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<ITrainBus>();
        OpenSharedTrain.Runs = 0;
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(IOpenSharedTrain).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
            }
        );
        metadata.TrainState = TrainState.InProgress;

        var act = () =>
            bus.RunByNameAsync<SharedOutput>(
                typeof(IOpenSharedTrain).FullName!,
                new SharedInput { Value = "x" },
                CancellationToken.None,
                metadata
            );

        await act.Should()
            .ThrowAsync<Trax.Core.Exceptions.TrainException>()
            .WithMessage("*state (InProgress), Must be Pending");
        OpenSharedTrain.Runs.Should().Be(0, "a record another run owns is not a parent link");
    }

    [Test]
    public async Task ITrainBus_RunByName_OnABusThatDoesNotImplementIt_ThrowsNotSupported()
    {
        using var scope = _serviceProvider.CreateScope();
        ITrainBus bus = new DelegatingBus(scope.ServiceProvider.GetRequiredService<ITrainBus>());

        var typed = () =>
            bus.RunByNameAsync<SharedOutput>(
                typeof(IOpenSharedTrain).FullName!,
                new SharedInput(),
                CancellationToken.None
            );
        var untyped = () =>
            bus.RunByNameAsync(
                typeof(IOpenSharedTrain).FullName!,
                new SharedInput(),
                CancellationToken.None
            );

        // A bus written before the by-name members existed still compiles, and says what it
        // lacks rather than running whichever train its input type maps to.
        await typed
            .Should()
            .ThrowAsync<NotSupportedException>()
            .WithMessage($"{nameof(DelegatingBus)} does not implement RunByNameAsync.");
        await untyped
            .Should()
            .ThrowAsync<NotSupportedException>()
            .WithMessage($"{nameof(DelegatingBus)} does not implement RunByNameAsync.");
    }

    [Test]
    public async Task LocalRunExecutor_WithAHostRegisteredBus_RunsByNameAsTheRecordItWrote()
    {
        using var scope = _serviceProvider.CreateScope();
        var bus = new ByNameDelegatingBus(scope.ServiceProvider.GetRequiredService<ITrainBus>());
        var executor = new LocalRunExecutor(
            bus,
            scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>()
        );

        var result = await executor.ExecuteAsync(
            typeof(IOpenSharedTrain).FullName!,
            new SharedInput { Value = "x" },
            typeof(SharedOutput)
        );

        result.Output.Should().Be(new SharedOutput("open"), $"the executor runs by name ({Adr})");
        bus.Received.Should()
            .ContainSingle("a bus the host registered is handed the record to run as")
            .Which.Should()
            .Be((typeof(IOpenSharedTrain).FullName!, result.MetadataId, TrainState.Pending));
    }

    [Test]
    public void Discovery_ListsBothTrains_EachPairedWithItsOwnImplementation()
    {
        var discovery = _serviceProvider.GetRequiredService<ITrainDiscoveryService>();

        var shared = discovery
            .DiscoverTrains()
            .Where(r => r.InputType == typeof(SharedInput))
            .ToList();

        shared
            .Select(r => (r.ServiceType, r.ImplementationType))
            .Should()
            .BeEquivalentTo(
                new[]
                {
                    (typeof(IOpenSharedTrain), typeof(OpenSharedTrain)),
                    (typeof(IGatedSharedTrain), typeof(GatedSharedTrain)),
                },
                $"discovery lists each train with its own implementation ({Adr})"
            );
        shared
            .Single(r => r.ServiceType == typeof(IGatedSharedTrain))
            .HasAuthorizeAttribute.Should()
            .BeTrue();
        shared
            .Single(r => r.ServiceType == typeof(IOpenSharedTrain))
            .HasAuthorizeAttribute.Should()
            .BeFalse();
    }

    [Test]
    public void Discovery_GatedInterfaceRegisteredAfterAnotherTrainsClass_KeepsItsOwnImplementation()
    {
        // The other ordering: the open train's class is registered first, then the gated train's
        // route, then the open train's route. Pairing one train's interface with another's class
        // would list the gated interface with the open train's (absent) requirements.
        var services = new ServiceCollection();
        services.AddTransient<OpenSharedTrain>();
        services.AddTransientTraxRoute<IGatedSharedTrain, GatedSharedTrain>();
        services.AddTransientTraxRoute<IOpenSharedTrain, OpenSharedTrain>();

        var registrations = new TrainDiscoveryService(services).DiscoverTrains();

        registrations
            .Select(r => (r.ServiceType, r.ImplementationType))
            .Should()
            .BeEquivalentTo(
                new[]
                {
                    (typeof(IOpenSharedTrain), typeof(OpenSharedTrain)),
                    (typeof(IGatedSharedTrain), typeof(GatedSharedTrain)),
                },
                $"discovery pairs from one train, whatever the registration order ({Adr})"
            );
        registrations
            .Single(r => r.ServiceType == typeof(IGatedSharedTrain))
            .RequiredRoles.Should()
            .Equal("Admin");
    }

    [Test]
    public void Discovery_InterfaceRegisteredWithItsImplementationType_IsPairedFromThatDescriptor()
    {
        var services = new ServiceCollection();
        services.AddTransient<OpenSharedTrain>();
        services.AddTransient<IGatedSharedTrain, GatedSharedTrain>();

        var registrations = new TrainDiscoveryService(services).DiscoverTrains();

        registrations
            .Select(r => (r.ServiceType, r.ImplementationType))
            .Should()
            .BeEquivalentTo(
                new[]
                {
                    (typeof(OpenSharedTrain), typeof(OpenSharedTrain)),
                    (typeof(IGatedSharedTrain), typeof(GatedSharedTrain)),
                }
            );
        registrations
            .Single(r => r.ServiceType == typeof(IGatedSharedTrain))
            .HasAuthorizeAttribute.Should()
            .BeTrue();
    }

    [Test]
    public void Discovery_InterfaceRegisteredOnlyByAFactory_IsListedWithItsOwnRequirements()
    {
        var services = new ServiceCollection();
        services.AddTransient<IGatedSharedTrain>(_ => new GatedSharedTrain());

        var registration = new TrainDiscoveryService(services)
            .DiscoverTrains()
            .Should()
            .ContainSingle()
            .Subject;

        registration.ServiceType.Should().Be(typeof(IGatedSharedTrain));
        registration
            .ImplementationType.Should()
            .Be(
                typeof(IGatedSharedTrain),
                "a factory's class cannot be read without running it, so the interface stands in"
            );
        registration
            .RequiredRoles.Should()
            .Equal(["Admin"], "the requirements declared on the interface still apply");
    }

    [Test]
    public void Discovery_InterfaceRegisteredWithAnInstance_IsPairedWithTheInstancesClass()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IOpenSharedTrain>(new OpenSharedTrain());

        new TrainDiscoveryService(services)
            .DiscoverTrains()
            .Should()
            .ContainSingle()
            .Which.ImplementationType.Should()
            .Be(typeof(OpenSharedTrain));
    }

    #region Buses

    /// <summary>
    /// A bus a host wrote before <c>RunByNameAsync</c> existed: it implements every member it had
    /// to then, and inherits the by-name defaults.
    /// </summary>
    private class DelegatingBus(ITrainBus inner) : ITrainBus
    {
        protected ITrainBus Inner { get; } = inner;

        public Task<TOut> RunAsync<TOut>(object trainInput, Metadata? metadata = null) =>
            Inner.RunAsync<TOut>(trainInput, metadata);

        public Task<TOut> RunAsync<TOut>(
            object trainInput,
            CancellationToken cancellationToken,
            Metadata? metadata = null
        ) => Inner.RunAsync<TOut>(trainInput, cancellationToken, metadata);

        public Task RunAsync(object trainInput, Metadata? metadata = null) =>
            Inner.RunAsync(trainInput, metadata);

        public Task RunAsync(
            object trainInput,
            CancellationToken cancellationToken,
            Metadata? metadata = null
        ) => Inner.RunAsync(trainInput, cancellationToken, metadata);

        public object InitializeTrain(object trainInput) => Inner.InitializeTrain(trainInput);
    }

    /// <summary>A host's bus that runs by name, recording the record each run was handed.</summary>
    private sealed class ByNameDelegatingBus(ITrainBus inner) : DelegatingBus(inner), ITrainBus
    {
        public List<(string Name, long MetadataId, TrainState State)> Received { get; } = [];

        public Task<TOut> RunByNameAsync<TOut>(
            string trainName,
            object trainInput,
            CancellationToken cancellationToken,
            Metadata? metadata = null
        )
        {
            var record =
                metadata
                ?? throw new InvalidOperationException(
                    "LocalRunExecutor hands a host's bus the record to run as"
                );
            Received.Add((trainName, record.Id, record.TrainState));
            return Inner.RunByNameAsync<TOut>(trainName, trainInput, cancellationToken, record);
        }
    }

    #endregion

    #region Trains

    public record SharedInput
    {
        public string Value { get; init; } = "";
    }

    public record SharedOutput(string RanBy);

    // Declared before the open train so the assembly scan meets it first.
    [TraxAuthorize(Roles = "Admin")]
    public interface IGatedSharedTrain : IServiceTrain<SharedInput, SharedOutput>;

    public class GatedSharedTrain : ServiceTrain<SharedInput, SharedOutput>, IGatedSharedTrain
    {
        protected override Task<Either<Exception, SharedOutput>> Junctions() =>
            Task.FromResult<Either<Exception, SharedOutput>>(new SharedOutput("gated"));
    }

    public interface IOpenSharedTrain : IServiceTrain<SharedInput, SharedOutput>;

    public class OpenSharedTrain : ServiceTrain<SharedInput, SharedOutput>, IOpenSharedTrain
    {
        public static int Runs;

        protected override Task<Either<Exception, SharedOutput>> Junctions()
        {
            Interlocked.Increment(ref Runs);
            return Task.FromResult<Either<Exception, SharedOutput>>(new SharedOutput("open"));
        }
    }

    #endregion
}
