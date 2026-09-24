using FluentAssertions;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Trax.Core.Exceptions;
using Trax.Core.Junction;
using Trax.Effect.Extensions;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Configuration;
using Trax.Mediator.Services.ChainVerification;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.UnitTests;

/// <summary>
/// The host reads every registered train's chain before it serves traffic.
///
/// <para>A chain names junction types, so two things are decidable at startup: that it can be
/// read as a declaration, and that every junction's input reaches Memory, or can be supplied by the
/// container, before it is needed. Each of those otherwise waits for something to run the train,
/// which for a rarely-taken train can be a long way from deployment. Whether a junction can be
/// constructed is deliberately not checked.</para>
///
/// <para>Enforces Trax.Docs/adr/0016-a-junction-chain-is-a-declaration-not-a-step-of-the-work.md.</para>
/// </summary>
[Property("adr", "Trax.Docs/adr/0016-a-junction-chain-is-a-declaration-not-a-step-of-the-work.md")]
[TestFixture]
public class TrainChainStartupValidatorTests
{
    /// <summary>
    /// Starts a validator that sees exactly one train. Discovery is substituted rather than
    /// scanned so each case is judged on its own train and not on the others in this assembly.
    /// </summary>
    private static Task<Exception?> Start<TService, TTrain>(
        bool skip = false,
        Action<IServiceCollection>? configure = null,
        RecordingLogger? logger = null,
        Func<IServiceScopeFactory, IServiceScopeFactory>? wrapScopes = null
    )
        where TService : class
        where TTrain : class, TService =>
        StartMany(
            [(typeof(TService), typeof(TTrain), Registration<TService, TTrain>())],
            skip,
            configure,
            logger,
            wrapScopes
        );

    private static async Task<Exception?> StartMany(
        (Type Service, Type Train, TrainRegistration Registration)[] trains,
        bool skip = false,
        Action<IServiceCollection>? configure = null,
        RecordingLogger? logger = null,
        Func<IServiceScopeFactory, IServiceScopeFactory>? wrapScopes = null
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax => trax.AddEffects(effects => effects));

        foreach (var (service, train, _) in trains)
            services.AddScoped(service, train);

        configure?.Invoke(services);

        await using var provider = services.BuildServiceProvider();

        var discovery = Substitute.For<ITrainDiscoveryService>();
        discovery.DiscoverTrains().Returns(trains.Select(t => t.Registration).ToList());

        var scopes = provider.GetRequiredService<IServiceScopeFactory>();

        var validator = new TrainChainStartupValidator(
            discovery,
            wrapScopes?.Invoke(scopes) ?? scopes,
            new MediatorConfiguration { SkipChainVerification = skip },
            logger
        );

        try
        {
            await validator.StartAsync(CancellationToken.None);
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private static TrainRegistration Registration<TService, TTrain>() =>
        new()
        {
            ServiceType = typeof(TService),
            ImplementationType = typeof(TTrain),
            InputType = typeof(ChainProbeInput),
            OutputType = typeof(bool),
            Lifetime = ServiceLifetime.Scoped,
            ServiceTypeName = typeof(TService).Name,
            ImplementationTypeName = typeof(TTrain).Name,
            InputTypeName = nameof(ChainProbeInput),
            OutputTypeName = nameof(Boolean),
            HasAllowAnonymousAttribute = true,
            RequiredPolicies = [],
            RequiredRoles = [],
            IsQuery = false,
            IsMutation = false,
            IsRemote = false,
            IsBroadcastEnabled = false,
            GraphQLOperations = 0,
        };

    [Test]
    public async Task Startup_WhenEveryChainLinesUp_Starts() =>
        (await Start<IWellFormedTrain, WellFormedTrain>()).Should().BeNull();

    [Test]
    public async Task Startup_WhenAJunctionsInputNeverReachesMemory_RefusesToStart()
    {
        var failure = await Start<IBrokenFlowTrain, BrokenFlowTrain>();

        failure.Should().BeOfType<TrainException>();
        failure!
            .Message.Should()
            .Contain(nameof(IBrokenFlowTrain))
            .And.Contain("Chain a junction that produces it first");
    }

    [Test]
    public async Task Startup_WhenAChainReadsItsInput_RefusesToStart()
    {
        var failure = await Start<IReadsInputTrain, ReadsInputTrain>();

        failure.Should().BeOfType<TrainException>();
        failure!.Message.Should().Contain("TrainInput");
    }

    [Test]
    public async Task Startup_WhenVerificationIsSkipped_StartsAnyway() =>
        (await Start<IBrokenFlowTrain, BrokenFlowTrain>(skip: true))
            .Should()
            .BeNull("the opt-out exists for the declared-versus-concrete blind spot");

    [Test]
    public async Task Startup_WhenAJunctionsInputComesFromTheContainer_Starts() =>
        (
            await Start<IContainerInputTrain, ContainerInputTrain>(configure: services =>
                services.AddSingleton<IChainProbeService, ChainProbeService>()
            )
        )
            .Should()
            .BeNull("a junction input not in Memory is resolved from the container at runtime");

    [Test]
    public async Task Startup_WhenAServiceCanOnlyBeBuiltInsideARequest_StillStarts() =>
        (
            await Start<IContainerInputTrain, ContainerInputTrain>(configure: services =>
                services.AddScoped<IChainProbeService>(_ =>
                    throw new InvalidOperationException("no HttpContext outside a request")
                )
            )
        )
            .Should()
            .BeNull(
                "whether the container can supply a type is answered without building one, so a "
                    + "request-only factory cannot crash startup"
            );

    [Test]
    public async Task Startup_WhenAChainSeedsAServiceWithAddServices_Starts() =>
        (await Start<ISeedingTrain, SeedingTrain>())
            .Should()
            .BeNull("a value handed to AddServices is in Memory for the junction after it");

    [Test]
    public async Task Startup_WhenAnAsyncChainReadsItsInput_RefusesToStart()
    {
        var failure = await Start<IAsyncReadsInputTrain, AsyncReadsInputTrain>();

        failure.Should().BeOfType<TrainException>();
        failure!
            .Message.Should()
            .Contain(
                "TrainInput",
                "an async body's exception lands in its task, and reading it as clean would hide it"
            );
    }

    [Test]
    public async Task Startup_WhenAChainAwaitsWorkBeforeDeclaring_RefusesToStart()
    {
        var failure = await Start<IAwaitingTrain, AwaitingTrain>();

        failure.Should().BeOfType<TrainException>();
        failure!.Message.Should().Contain("awaited something");
    }

    [Test]
    public async Task Startup_WhenATrainCanOnlyBeBuiltInsideARequest_StartsAndSkipsIt()
    {
        var logger = new RecordingLogger();

        var failure = await Start<IRequestBoundTrain, RequestBoundTrain>(
            configure: RequestOnlyService,
            logger: logger
        );

        failure
            .Should()
            .BeNull(
                "a train that cannot be built at boot is not evidence of a chain that cannot run, "
                    + "and refusing to start over it would break hosts that ran fine before"
            );
        logger
            .Warnings.Should()
            .ContainSingle("a skipped train is reported, so a skip can be told from a pass")
            .Which.Should()
            .Contain(nameof(IRequestBoundTrain))
            .And.Contain("could not be constructed outside a request")
            .And.Contain("no HttpContext outside a request");
    }

    [Test]
    public async Task Startup_WhenATrainIsSkipped_StillChecksTheTrainsAfterIt()
    {
        var logger = new RecordingLogger();

        var failure = await StartMany(
            [
                (
                    typeof(IRequestBoundTrain),
                    typeof(RequestBoundTrain),
                    Registration<IRequestBoundTrain, RequestBoundTrain>()
                ),
                (
                    typeof(IBrokenFlowTrain),
                    typeof(BrokenFlowTrain),
                    Registration<IBrokenFlowTrain, BrokenFlowTrain>()
                ),
            ],
            configure: RequestOnlyService,
            logger: logger
        );

        failure.Should().BeOfType<TrainException>("the broken train after the skipped one is read");
        failure!
            .Message.Should()
            .Contain("1 of 1", "the skipped train is not counted as checked")
            .And.Contain(nameof(IBrokenFlowTrain))
            .And.NotContain(nameof(IRequestBoundTrain));
        logger.Warnings.Should().ContainSingle().Which.Should().Contain(nameof(IRequestBoundTrain));
    }

    private static void RequestOnlyService(IServiceCollection services) =>
        services.AddScoped<IRequestOnlyService>(_ =>
            throw new InvalidOperationException("no HttpContext outside a request")
        );

    [Test]
    public async Task Startup_WhenSeveralTrainsCannotRun_ReportsEveryOne()
    {
        var failure = await StartMany([
            (
                typeof(IBrokenFlowTrain),
                typeof(BrokenFlowTrain),
                Registration<IBrokenFlowTrain, BrokenFlowTrain>()
            ),
            (
                typeof(IAwaitingTrain),
                typeof(AwaitingTrain),
                Registration<IAwaitingTrain, AwaitingTrain>()
            ),
            (
                typeof(IWellFormedTrain),
                typeof(WellFormedTrain),
                Registration<IWellFormedTrain, WellFormedTrain>()
            ),
        ]);

        failure!
            .Message.Should()
            .Contain("2 of 3", "one start reports every train rather than one per attempt")
            .And.Contain(nameof(IBrokenFlowTrain))
            .And.Contain(nameof(IAwaitingTrain));
    }

    [Test]
    public async Task Startup_WhenARegisteredServiceIsNotATrain_StartsAndSkipsIt()
    {
        var logger = new RecordingLogger();

        var failure = await Start<INotATrain, NotATrain>(logger: logger);

        failure.Should().BeNull("a registered train need not derive from Train<,>");
        logger
            .Warnings.Should()
            .ContainSingle()
            .Which.Should()
            .Contain(nameof(INotATrain))
            .And.Contain("does not derive from Train<TIn, TOut>");
    }

    [Test]
    public async Task Startup_WhenJunctionsThrowsSomethingOtherThanADeclarationError_RefusesToStart()
    {
        var failure = await Start<IThrowingDeclarationTrain, ThrowingDeclarationTrain>();

        failure.Should().BeOfType<TrainException>();
        failure!
            .Message.Should()
            .Contain($"{nameof(IThrowingDeclarationTrain)}: its chain could not be read (");
    }

    [Test]
    public async Task Startup_WhenVerifyingAChainThrows_RefusesToStart()
    {
        // BrokenFlowTrain's first junction needs an int that is not in Memory, so the check asks
        // the container for one, and that question is what throws here.
        var failure = await Start<IBrokenFlowTrain, BrokenFlowTrain>(
            wrapScopes: scopes => new ThrowingIsServiceScopeFactory(scopes)
        );

        failure.Should().BeOfType<TrainException>();
        failure!
            .Message.Should()
            .Contain(
                $"{nameof(IBrokenFlowTrain)}: its chain could not be verified "
                    + $"({ThrowingIsService.Reason})"
            );
    }

    /// <summary>Keeps the warnings the validator logs, so a skip can be told from a pass.</summary>
    public sealed class RecordingLogger : ILogger<TrainChainStartupValidator>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            if (logLevel == LogLevel.Warning)
                Warnings.Add(formatter(state, exception));
        }
    }

    /// <summary>
    /// Hands the validator scopes whose container answers "is this a service?" by throwing, the
    /// only way <c>ChainVerification.Verify</c> itself can fail on a well-formed chain.
    /// </summary>
    private sealed class ThrowingIsServiceScopeFactory(IServiceScopeFactory inner)
        : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new Scope(inner.CreateScope());

        private sealed class Scope(IServiceScope inner) : IServiceScope
        {
            public IServiceProvider ServiceProvider { get; } = new Provider(inner.ServiceProvider);

            public void Dispose() => inner.Dispose();
        }

        private sealed class Provider(IServiceProvider inner) : IServiceProvider
        {
            public object? GetService(Type serviceType) =>
                serviceType == typeof(IServiceProviderIsService)
                    ? new ThrowingIsService()
                    : inner.GetService(serviceType);
        }
    }

    private sealed class ThrowingIsService : IServiceProviderIsService
    {
        public const string Reason = "the container could not answer";

        public bool IsService(Type serviceType) => throw new InvalidOperationException(Reason);
    }

    /// <summary>
    /// A dedicated input type. These trains are discovered by assembly scan like any other, so a
    /// shared input type such as string would register a train for it and change what other
    /// tests in this assembly see.
    /// </summary>
    public record ChainProbeInput(string Value);

    private class TextToNumber : Junction<ChainProbeInput, int>
    {
        public override Task<int> Run(ChainProbeInput input) => Task.FromResult(input.Value.Length);
    }

    private class NumberToFlag : Junction<int, bool>
    {
        public override Task<bool> Run(int input) => Task.FromResult(input > 0);
    }

    public interface IWellFormedTrain : IServiceTrain<ChainProbeInput, bool>;

    public class WellFormedTrain : ServiceTrain<ChainProbeInput, bool>, IWellFormedTrain
    {
        protected override Task<Either<Exception, bool>> Junctions() =>
            Chain<TextToNumber>().Chain<NumberToFlag>().Resolve();
    }

    public interface IBrokenFlowTrain : IServiceTrain<ChainProbeInput, bool>;

    public class BrokenFlowTrain : ServiceTrain<ChainProbeInput, bool>, IBrokenFlowTrain
    {
        // NumberToFlag needs an int and nothing before it produces one.
        protected override Task<Either<Exception, bool>> Junctions() =>
            Chain<NumberToFlag>().Resolve();
    }

    public interface IReadsInputTrain : IServiceTrain<ChainProbeInput, bool>;

    public class ReadsInputTrain : ServiceTrain<ChainProbeInput, bool>, IReadsInputTrain
    {
        protected override Task<Either<Exception, bool>> Junctions() =>
            TrainInput.Value.Length > 0
                ? Chain<TextToNumber>().Chain<NumberToFlag>().Resolve()
                : Chain<TextToNumber>().Chain<NumberToFlag>().Resolve();
    }

    public interface IChainProbeService;

    public class ChainProbeService : IChainProbeService;

    private class ServiceToFlag : Junction<IChainProbeService, bool>
    {
        public override Task<bool> Run(IChainProbeService input) => Task.FromResult(true);
    }

    public interface IContainerInputTrain : IServiceTrain<ChainProbeInput, bool>;

    public class ContainerInputTrain : ServiceTrain<ChainProbeInput, bool>, IContainerInputTrain
    {
        protected override Task<Either<Exception, bool>> Junctions() =>
            Chain<ServiceToFlag>().Resolve();
    }

    public interface ISeedingTrain : IServiceTrain<ChainProbeInput, bool>;

    public class SeedingTrain : ServiceTrain<ChainProbeInput, bool>, ISeedingTrain
    {
        protected override Task<Either<Exception, bool>> Junctions() =>
            AddServices<IChainProbeService>(new ChainProbeService())
                .Chain<ServiceToFlag>()
                .Resolve();
    }

    public interface IAsyncReadsInputTrain : IServiceTrain<ChainProbeInput, bool>;

    public class AsyncReadsInputTrain : ServiceTrain<ChainProbeInput, bool>, IAsyncReadsInputTrain
    {
        protected override async Task<Either<Exception, bool>> Junctions() =>
            TrainInput.Value.Length > 0
                ? await Chain<TextToNumber>().Chain<NumberToFlag>().Resolve()
                : await Chain<TextToNumber>().Chain<NumberToFlag>().Resolve();
    }

    public interface IAwaitingTrain : IServiceTrain<ChainProbeInput, bool>;

    public class AwaitingTrain : ServiceTrain<ChainProbeInput, bool>, IAwaitingTrain
    {
        protected override async Task<Either<Exception, bool>> Junctions()
        {
            await Task.Yield();
            return await Chain<TextToNumber>().Chain<NumberToFlag>().Resolve();
        }
    }

    public interface INotATrain;

    public class NotATrain : INotATrain;

    public interface IThrowingDeclarationTrain : IServiceTrain<ChainProbeInput, bool>;

    public class ThrowingDeclarationTrain
        : ServiceTrain<ChainProbeInput, bool>,
            IThrowingDeclarationTrain
    {
        public const string Reason = "the declaration itself failed";

        // Throws synchronously, before any chain is recorded, and not as a ChainDeclarationException.
        protected override Task<Either<Exception, bool>> Junctions() =>
            throw new InvalidOperationException(Reason);
    }

    public interface IRequestOnlyService;

    public interface IRequestBoundTrain : IServiceTrain<ChainProbeInput, bool>;

    public class RequestBoundTrain(IRequestOnlyService requestOnly)
        : ServiceTrain<ChainProbeInput, bool>,
            IRequestBoundTrain
    {
        public IRequestOnlyService RequestOnly { get; } = requestOnly;

        protected override Task<Either<Exception, bool>> Junctions() =>
            Chain<TextToNumber>().Chain<NumberToFlag>().Resolve();
    }
}
