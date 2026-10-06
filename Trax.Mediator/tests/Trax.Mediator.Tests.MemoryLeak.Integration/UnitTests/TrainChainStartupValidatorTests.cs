using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Trax.Core.Exceptions;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
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
/// which for a rarely-taken train can be a long way from deployment. A junction Trax cannot build
/// (not exactly one public constructor), or whose constructor needs something neither Memory nor
/// the container holds, is refused as well. A train that can never be built (no public
/// constructor, or an unregistered type in its constructor or its dependencies' constructors) is
/// refused, and one that needs a registered type only a request can build is skipped with a
/// warning.</para>
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
        Func<IServiceScopeFactory, IServiceScopeFactory>? wrapScopes = null,
        CancellationToken token = default
    )
        where TService : class
        where TTrain : class, TService =>
        StartMany(
            [(typeof(TService), typeof(TTrain), Registration<TService, TTrain>())],
            skip,
            configure,
            logger,
            wrapScopes,
            token
        );

    private static async Task<Exception?> StartMany(
        (Type Service, Type Train, TrainRegistration Registration)[] trains,
        bool skip = false,
        Action<IServiceCollection>? configure = null,
        RecordingLogger? logger = null,
        Func<IServiceScopeFactory, IServiceScopeFactory>? wrapScopes = null,
        CancellationToken token = default
    )
    {
        var services = new ServiceCollection();
        // AddMediator registers the collection too; the check follows dependencies through it.
        services.AddSingleton<IServiceCollection>(services);
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
            await validator.StartingAsync(token);
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private static TrainRegistration Registration<TService, TTrain>(
        Type? implementationType = null,
        Type? inputType = null
    ) =>
        new()
        {
            ServiceType = typeof(TService),
            ImplementationType = implementationType ?? typeof(TTrain),
            InputType = inputType ?? typeof(ChainProbeInput),
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
    public async Task Startup_WhenEveryChainLinesUp_WarnsAboutNothing()
    {
        var logger = new RecordingLogger();

        (await Start<IWellFormedTrain, WellFormedTrain>(logger: logger)).Should().BeNull();
        logger.Warnings.Should().BeEmpty();
    }

    [Test]
    public async Task Startup_WhenAnInjectPropertysTypeIsNotRegistered_RefusesNamingTheType()
    {
        var failure = await Start<IInjectsUnregisteredTrain, InjectsUnregisteredTrain>();

        failure.Should().BeOfType<TrainException>();
        failure!
            .Message.Should()
            .Contain(
                $"{nameof(IInjectsUnregisteredTrain)}: its [Inject] property "
                    + $"'{nameof(InjectsUnregisteredTrain.Probe)}' needs "
                    + $"'{nameof(IUnregisteredProbeService)}', which is not registered",
                "the property would be null on every run, which is a train that cannot run"
            );
    }

    [Test]
    public async Task Startup_WhenANullableInjectPropertysTypeIsNotRegistered_StartsAndWarns()
    {
        var logger = new RecordingLogger();

        var failure = await Start<IOptionallyInjectsTrain, OptionallyInjectsTrain>(logger: logger);

        failure
            .Should()
            .BeNull("a property declared nullable says the train runs without the dependency");
        logger
            .Warnings.Should()
            .ContainSingle()
            .Which.Should()
            .Contain(nameof(IOptionallyInjectsTrain))
            .And.Contain($"'{nameof(IUnregisteredProbeService)}', which is not registered")
            .And.Contain("declared nullable");
    }

    [Test]
    public async Task Startup_WhenAnInjectPropertysTypeIsRegistered_Starts()
    {
        var logger = new RecordingLogger();

        var failure = await Start<IInjectsUnregisteredTrain, InjectsUnregisteredTrain>(
            configure: services =>
                services.AddScoped<IUnregisteredProbeService, UnregisteredProbeService>(),
            logger: logger
        );

        failure.Should().BeNull();
        logger.Warnings.Should().BeEmpty();
    }

    [Test]
    public async Task Startup_WhenAnInjectPropertysRegisteredTypeCanNeverBeBuilt_RefusesEvenIfNullable()
    {
        var failure = await Start<
            IOptionallyInjectsRepositoryTrain,
            OptionallyInjectsRepositoryTrain
        >(configure: services => services.AddScoped<IProbeRepository, ProbeRepository>());

        failure
            .Should()
            .BeOfType<TrainException>(
                "GetService throws for a registered type it cannot build, so resolving the "
                    + "train fails on every run however the property is declared"
            );
        failure!
            .Message.Should()
            .Contain(
                $"needs '{nameof(IUnregisteredProbeService)}', which is not registered, and the "
                    + "train's [Inject] property "
                    + $"'{nameof(OptionallyInjectsRepositoryTrain.Repository)}' reaches it through "
                    + $"'{nameof(IProbeRepository)}'"
            );
    }

    [Test]
    public async Task Startup_WhenAnInjectPropertyIsSetByItsInitializer_Starts()
    {
        // InjectProperties skips a property that already holds a value, so this train runs with
        // its own clock and the container never needs to register one.
        var logger = new RecordingLogger();

        var failure = await Start<IInitializedInjectTrain, InitializedInjectTrain>(logger: logger);

        failure.Should().BeNull("the property holds a value on every run, so nothing is unfilled");
        logger.Warnings.Should().BeEmpty();
    }

    [Test]
    public async Task Startup_WhenAnInjectPropertyIsSetByTheConstructor_Starts()
    {
        var logger = new RecordingLogger();

        var failure = await Start<IConstructorSetInjectTrain, ConstructorSetInjectTrain>(
            logger: logger
        );

        failure.Should().BeNull("the property holds a value on every run, so nothing is unfilled");
        logger.Warnings.Should().BeEmpty();
    }

    [Test]
    public async Task Startup_WhenAnInitializerSetsAPropertyWhoseRegisteredTypeCannotBeBuilt_Starts()
    {
        // Registered the way AddMediator registers a train, so the container fills its
        // properties. Filling skips the initialized one, so GetService never builds the
        // repository and the train resolves.
        var failure = await Start<IInitializedRepositoryTrain, InitializedRepositoryTrain>(
            configure: services =>
            {
                services.AddScoped<IProbeRepository, ProbeRepository>();
                services.AddScopedTraxRoute<
                    IInitializedRepositoryTrain,
                    InitializedRepositoryTrain
                >();
            }
        );

        failure.Should().BeNull();
    }

    [Test]
    public async Task Startup_WhenFillingAnInjectPropertyFails_RefusesNamingOnlyThatProperty()
    {
        var failure = await Start<IFillFailsTrain, FillFailsTrain>(configure: services =>
        {
            services.AddScoped<IProbeRepository, ProbeRepository>();
            services.AddScopedTraxRoute<IFillFailsTrain, FillFailsTrain>();
        });

        failure.Should().BeOfType<TrainException>();
        failure!
            .Message.Should()
            .Contain($"'{nameof(FillFailsTrain.Repository)}' reaches it through")
            .And.NotContain(
                $"'{nameof(FillFailsTrain.Clock)}'",
                "the clock is set by its initializer, which filling leaves alone"
            );
    }

    [Test]
    public async Task Startup_WhenATrainCannotBeBuiltAtBoot_ItsInjectPropertiesAreOnlyWarnedAbout()
    {
        // Without an instance there is no telling whether the constructor sets the property, so
        // refusing would stop a host that may run fine.
        var logger = new RecordingLogger();

        var failure = await Start<IRequestBoundInjectingTrain, RequestBoundInjectingTrain>(
            configure: RequestOnlyService,
            logger: logger
        );

        failure.Should().BeNull();
        logger
            .Warnings.Should()
            .Contain(w =>
                w.Contains($"'{nameof(RequestBoundInjectingTrain.Probe)}' needs")
                && w.Contains("could not be built here")
            );
    }

    [Test]
    public async Task Startup_WhenVerificationIsSkipped_TheWarningSaysTheInjectCheckIsOffToo()
    {
        var logger = new RecordingLogger();

        (
            await Start<IInjectsUnregisteredTrain, InjectsUnregisteredTrain>(
                skip: true,
                logger: logger
            )
        )
            .Should()
            .BeNull();
        logger.Warnings.Should().ContainSingle().Which.Should().Contain("[Inject] properties");
    }

    [Test]
    [NonParallelizable]
    public async Task Startup_WhenNullabilityMetadataIsTurnedOff_AnUnknownPropertyIsOnlyWarnedAbout()
    {
        // A trimmed app sets this switch off, and then every property reads as Unknown, a T?
        // included. Unknown is otherwise what a project without nullable annotations gives,
        // which is refused, so the same train is checked both ways.
        const string Switch = "System.Reflection.NullabilityInfoContext.IsSupported";

        // NullabilityInfoContext reads the switch once; read it before changing it, so the
        // change reaches only the validator.
        _ = new System.Reflection.NullabilityInfoContext().Create(
            typeof(ObliviousInjectTrain).GetProperty(nameof(ObliviousInjectTrain.Probe))!
        );

        (await Start<IObliviousInjectTrain, ObliviousInjectTrain>())
            .Should()
            .BeOfType<TrainException>("with the metadata on, Unknown means no annotations");

        var logger = new RecordingLogger();
        AppContext.SetSwitch(Switch, false);
        try
        {
            (await Start<IObliviousInjectTrain, ObliviousInjectTrain>(logger: logger))
                .Should()
                .BeNull("with the metadata off, Unknown says nothing about the declaration");
        }
        finally
        {
            AppContext.SetSwitch(Switch, true);
        }

        logger.Warnings.Should().ContainSingle().Which.Should().Contain("declared nullable");
    }

    [Test]
    public async Task Startup_WhenAChainBuiltJunctionDeclaresInjectProperties_StartsAndWarns()
    {
        var logger = new RecordingLogger();

        var failure = await Start<IChainsInjectingJunctionTrain, ChainsInjectingJunctionTrain>(
            logger: logger
        );

        failure
            .Should()
            .BeNull(
                "whether the junction reads the property cannot be told from its type, and one "
                    + "that guards against null runs fine"
            );
        logger
            .Warnings.Should()
            .ContainSingle()
            .Which.Should()
            .Contain(nameof(IChainsInjectingJunctionTrain))
            .And.Contain($"Chain<{nameof(InjectingJunction)}>")
            .And.Contain($"'{nameof(InjectingJunction.Probe)}' is null whenever it runs");
    }

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
            .Contain($"{nameof(IThrowingDeclarationTrain)}: its chain could not be read (")
            .And.Contain(
                ThrowingDeclarationTrain.Reason,
                "the reflection wrapper says only that an invocation target threw, which leaves "
                    + "the operator no way to tell what is actually wrong"
            );
    }

    [Test]
    public async Task Startup_WhenVerifyingAChainThrows_RefusesToStart()
    {
        // BrokenFlowTrain's first junction needs an int that is not in Memory, so the check asks
        // the container for one, and that question is what throws here.
        var failure = await Start<IBrokenFlowTrain, BrokenFlowTrain>(
            wrapScopes: scopes => new ReplacedIsServiceScopeFactory(scopes, new ThrowingIsService())
        );

        failure.Should().BeOfType<TrainException>();
        failure!
            .Message.Should()
            .Contain(
                $"{nameof(IBrokenFlowTrain)}: its chain could not be verified "
                    + $"({ThrowingIsService.Reason})"
            );
    }

    [Test]
    public async Task Startup_WhenATrainDependsOnAnAsyncOnlyDisposable_Starts() =>
        (
            await Start<IAsyncDisposingTrain, AsyncDisposingTrain>(configure: services =>
                services.AddScoped<IAsyncOnlyService, AsyncOnlyService>()
            )
        )
            .Should()
            .BeNull(
                "a request scope disposes such a dependency asynchronously, so the same train "
                    + "runs fine; refusing the host over it reports a disposal problem as a "
                    + "chain problem, and the only workaround is to turn the check off"
            );

    [Test]
    public async Task Startup_WhenTheHostIsAlreadyStopping_StopsRatherThanWorkingThrough()
    {
        using var stopping = new CancellationTokenSource();
        await stopping.CancelAsync();

        var outcome = await Start<IBrokenFlowTrain, BrokenFlowTrain>(token: stopping.Token);

        outcome
            .Should()
            .BeAssignableTo<OperationCanceledException>(
                "a host already shutting down should stop reading chains rather than work through "
                    + "every registered train; this train's chain cannot run, so reporting that "
                    + "instead would mean the check ran anyway"
            );
    }

    [Test]
    public async Task Startup_WhenATrainNeedsAServiceThatIsNotRegistered_RefusesToStartNamingIt()
    {
        var logger = new RecordingLogger();

        var failure = await Start<INeedsUnregisteredTrain, NeedsUnregisteredTrain>(logger: logger);

        failure
            .Should()
            .BeOfType<TrainException>(
                "a missing registration fails every run of the train, which is not the same as a "
                    + "dependency only a request can supply"
            );
        failure!
            .Message.Should()
            .Contain(
                $"{nameof(INeedsUnregisteredTrain)} cannot be built: its constructor needs "
                    + $"'{nameof(IUnregisteredProbeService)}', which is not registered. "
                    + "Register it before building the host."
            );
        logger.Warnings.Should().BeEmpty("a refused train is not also reported as skipped");
    }

    [Test]
    public async Task Startup_NamesTheFaultyStepFromOneAndItsJunction()
    {
        var failure = await Start<IBrokenFlowTrain, BrokenFlowTrain>();

        failure!
            .Message.Should()
            .Contain(
                $"{nameof(IBrokenFlowTrain)}: step 1 ({nameof(NumberToFlag)}) needs",
                "a 0-based index and no junction name leaves the reader counting steps by hand"
            );
    }

    [Test]
    public async Task Startup_WhenAChainNamesSomethingThatIsNotAJunction_ReportsThatFirstWithoutTheResolveCascade()
    {
        var failure = await Start<INotAJunctionTrain, NotAJunctionTrain>();

        failure.Should().BeOfType<TrainException>();

        var lines = failure!
            .Message.Split(Environment.NewLine)
            .Where(line => line.Contains(nameof(INotAJunctionTrain)))
            .ToList();

        lines
            .Should()
            .ContainSingle(
                "the Resolve fault is a consequence of the refused step, and fixing that step is "
                    + "what the reader has to do"
            )
            .Which.Should()
            .Contain(nameof(NotAJunction))
            .And.Contain("does not implement IJunction<TIn, TOut>")
            .And.NotContain("the chain ends without");
    }

    [Test]
    public async Task Startup_WhenATrainHasSeveralFaults_PutsEachOnItsOwnLineRefusalsFirst()
    {
        var failure = await Start<ISeveralFaultsTrain, SeveralFaultsTrain>();

        var lines = failure!
            .Message.Split(Environment.NewLine)
            .Where(line => line.Contains(nameof(ISeveralFaultsTrain)))
            .ToList();

        lines.Should().HaveCount(2);
        lines[0]
            .Should()
            .Contain(
                $"step 1: Chain names {nameof(NotAJunction)}",
                "a refusal comes before step faults"
            );
        lines[1]
            .Should()
            .Contain(
                $"step 2 ({nameof(NumberToFlag)}) needs",
                "a step keeps its written position when a step before it is refused"
            );
    }

    [Test]
    public async Task Startup_WhenAJunctionHasTwoConstructors_RefusesToStartNamingIt()
    {
        var failure = await Start<ITwoConstructorJunctionTrain, TwoConstructorJunctionTrain>();

        failure
            .Should()
            .BeOfType<TrainException>("Trax builds a junction through its single constructor");
        failure!
            .Message.Should()
            .Contain(nameof(ITwoConstructorJunctionTrain))
            .And.Contain(nameof(TwoConstructorJunction));
    }

    [Test]
    public async Task Startup_WhenATrainNeedsSeveralUnregisteredServices_NamesEachOneReadably()
    {
        var failure = await Start<INeedsSeveralUnregisteredTrain, NeedsSeveralUnregisteredTrain>();

        failure.Should().BeOfType<TrainException>();
        failure!
            .Message.Should()
            .Contain(
                $"{nameof(INeedsSeveralUnregisteredTrain)} cannot be built: its constructor needs "
                    + $"'{nameof(IUnregisteredProbeService)}', 'IGenericProbe<Int32>', "
                    + "'GenericProbeHolder<String>.INestedProbe', which are not registered. "
                    + "Register them before building the host.",
                "a generic type is written with its arguments rather than as IGenericProbe`1, and "
                    + "a type nested in a generic one is written with the outer type that owns the "
                    + "argument"
            );
    }

    [Test]
    public async Task Startup_WhenOnlyADefaultedOrKeyedArgumentIsUnregistered_SkipsRatherThanRefuses()
    {
        var logger = new RecordingLogger();

        var failure = await Start<IOptionalArgumentsTrain, OptionalArgumentsTrain>(logger: logger);

        failure
            .Should()
            .BeNull(
                "a defaulted argument need not be registered and a keyed one is not answered by "
                    + "IsService, so neither is evidence the train can never be built"
            );
        logger
            .Warnings.Should()
            .ContainSingle()
            .Which.Should()
            .Contain(nameof(IOptionalArgumentsTrain))
            .And.Contain("could not be constructed outside a request");
    }

    [Test]
    public async Task Startup_WhenATrainRegisteredOnlyByAFactoryCannotBeBuilt_SkipsIt()
    {
        var logger = new RecordingLogger();
        // What discovery lists for an interface whose only registration is a factory.
        var registration = Registration<IRequestBoundTrain, RequestBoundTrain>(
            implementationType: typeof(IRequestBoundTrain)
        );

        var failure = await StartMany(
            [(typeof(IRequestBoundTrain), typeof(RequestBoundTrain), registration)],
            configure: services =>
                services.AddScoped<IRequestBoundTrain>(_ =>
                    throw new InvalidOperationException("no HttpContext outside a request")
                ),
            logger: logger
        );

        failure
            .Should()
            .BeNull(
                "there is no constructor to read for a factory, so nothing names a missing type"
            );
        logger
            .Warnings.Should()
            .ContainSingle()
            .Which.Should()
            .Contain(nameof(IRequestBoundTrain))
            .And.Contain("no HttpContext outside a request");
    }

    [Test]
    public async Task Startup_WhenTheContainerCannotSayWhatIsRegistered_SkipsATrainItCannotBuild()
    {
        var logger = new RecordingLogger();

        var failure = await Start<INeedsUnregisteredTrain, NeedsUnregisteredTrain>(
            logger: logger,
            wrapScopes: scopes => new ReplacedIsServiceScopeFactory(scopes, null)
        );

        failure
            .Should()
            .BeNull(
                "without IServiceProviderIsService a missing registration cannot be told from a "
                    + "request-only dependency, and the check only refuses what it can prove"
            );
        logger
            .Warnings.Should()
            .ContainSingle()
            .Which.Should()
            .Contain(nameof(INeedsUnregisteredTrain));
    }

    [Test]
    public async Task Startup_WhenTheContainerThrowsAnsweringForATrainItCannotBuild_SkipsIt()
    {
        var logger = new RecordingLogger();

        var failure = await Start<INeedsUnregisteredTrain, NeedsUnregisteredTrain>(
            logger: logger,
            wrapScopes: scopes => new ReplacedIsServiceScopeFactory(scopes, new ThrowingIsService())
        );

        failure
            .Should()
            .BeNull("a container that cannot answer is not evidence the train cannot run");
        logger
            .Warnings.Should()
            .ContainSingle()
            .Which.Should()
            .Contain(nameof(INeedsUnregisteredTrain));
    }

    [Test]
    public async Task Startup_WhenAChainEndsWithoutItsResult_NamesTheResolveStep()
    {
        var failure = await Start<INoResultTrain, NoResultTrain>();

        failure.Should().BeOfType<TrainException>();
        failure!
            .Message.Should()
            .Contain(
                $"{nameof(INoResultTrain)}: step 2 (Resolve) the chain ends without",
                "a step with no junction is named by its kind"
            );
    }

    [Test]
    public async Task Startup_WhenATrainsInputHasMoreThanSevenElements_ReportsTheInputNotAStep()
    {
        var registration = Registration<IWideInputTrain, WideInputTrain>(
            inputType: typeof((int, int, int, int, int, int, int, int))
        );

        var failure = await StartMany([
            (typeof(IWideInputTrain), typeof(WideInputTrain), registration),
        ]);

        failure.Should().BeOfType<TrainException>();
        failure!
            .Message.Split(Environment.NewLine)
            .Should()
            .Contain(
                line => line.StartsWith($"  - {nameof(IWideInputTrain)}: the train's input '"),
                "the fault is about the train's input, which no step number describes"
            )
            .Which.Should()
            .Contain("holds more than seven elements");
    }

    [Test]
    public async Task Startup_WhenARefusedStepStillProducesItsOutput_ReportsAnIndependentResolveFault()
    {
        var failure = await Start<IRefusalAndNoResultTrain, RefusalAndNoResultTrain>();

        var lines = failure!
            .Message.Split(Environment.NewLine)
            .Where(line => line.Contains(nameof(IRefusalAndNoResultTrain)))
            .ToList();

        lines.Should().Contain(line => line.Contains("step 1: IChain<TextToNumber> names a class"));
        lines
            .Should()
            .Contain(
                line => line.Contains("step 2 (Resolve) the chain ends without"),
                "the refused step still leaves its int in Memory, so the missing bool is a fault of "
                    + "its own rather than a consequence, and hiding it costs another deployment"
            );
    }

    [Test]
    public async Task Startup_WhenAJunctionsConstructorNeedsAnUnregisteredService_RefusesToStartNamingBoth()
    {
        var failure = await Start<
            IJunctionNeedsUnregisteredTrain,
            JunctionNeedsUnregisteredTrain
        >();

        failure.Should().BeOfType<TrainException>();
        failure!
            .Message.Should()
            .Contain(
                $"{nameof(IJunctionNeedsUnregisteredTrain)}: step 1 ({nameof(NeedsProbeJunction)}) needs '",
                "a junction that cannot be handed its constructor argument fails every run"
            )
            .And.Contain($"{nameof(IUnregisteredProbeService)}' as a constructor argument");
    }

    [Test]
    public async Task Startup_WhenATrainsDependencyNeedsAnUnregisteredService_RefusesToStartNamingThePath()
    {
        var logger = new RecordingLogger();

        var failure = await Start<INeedsNestedUnregisteredTrain, NeedsNestedUnregisteredTrain>(
            configure: services => services.AddScoped<IProbeRepository, ProbeRepository>(),
            logger: logger
        );

        failure
            .Should()
            .BeOfType<TrainException>(
                "the train's own argument is registered, but building it needs a type nothing "
                    + "registers, so the train fails on every run"
            );
        failure!
            .Message.Should()
            .Contain(
                $"{nameof(INeedsNestedUnregisteredTrain)} cannot be built: {nameof(ProbeRepository)} "
                    + $"needs '{nameof(IUnregisteredProbeService)}', which is not registered, and "
                    + $"the train's constructor reaches it through '{nameof(IProbeRepository)}'. "
                    + "Register it before building the host."
            );
        logger.Warnings.Should().BeEmpty("a refused train is not also reported as skipped");
    }

    [Test]
    public async Task Startup_WhenATrainsDependencyIsBuiltByAFactory_SkipsRatherThanRefuses()
    {
        var logger = new RecordingLogger();

        var failure = await Start<INeedsNestedUnregisteredTrain, NeedsNestedUnregisteredTrain>(
            configure: services =>
                services.AddScoped<IProbeRepository>(_ =>
                    throw new InvalidOperationException("no HttpContext outside a request")
                ),
            logger: logger
        );

        failure
            .Should()
            .BeNull("a factory cannot be followed, so nothing proves the train can never be built");
        logger.Warnings.Should().ContainSingle().Which.Should().Contain("no HttpContext");
    }

    [Test]
    public async Task Startup_WhenATrainHasNoPublicConstructor_RefusesToStart()
    {
        var failure = await Start<INoPublicConstructorTrain, NoPublicConstructorTrain>();

        failure
            .Should()
            .BeOfType<TrainException>(
                "the container cannot build a class with no public constructor"
            );
        failure!
            .Message.Should()
            .Contain(
                $"{nameof(INoPublicConstructorTrain)} cannot be built: "
                    + $"{nameof(NoPublicConstructorTrain)} has no public constructor. Give it one."
            );
    }

    [Test]
    public async Task Startup_WhenTheHostCallsOnlyStartAsync_StillRefuses()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax => trax.AddEffects(effects => effects));
        services.AddScoped<IBrokenFlowTrain, BrokenFlowTrain>();
        await using var provider = services.BuildServiceProvider();

        var discovery = Substitute.For<ITrainDiscoveryService>();
        discovery.DiscoverTrains().Returns([Registration<IBrokenFlowTrain, BrokenFlowTrain>()]);

        var validator = new TrainChainStartupValidator(
            discovery,
            provider.GetRequiredService<IServiceScopeFactory>(),
            new MediatorConfiguration()
        );

        var act = () => validator.StartAsync(CancellationToken.None);

        await act.Should()
            .ThrowAsync<TrainException>(
                "a custom host or a test harness that calls only StartAsync would otherwise run "
                    + "with no check at all"
            );
    }

    [Test]
    public async Task Startup_WhenStartingAsyncAlreadyChecked_StartAsyncDoesNotCheckAgain()
    {
        var discovery = Substitute.For<ITrainDiscoveryService>();
        discovery.DiscoverTrains().Returns([]);
        var services = new ServiceCollection();
        await using var provider = services.BuildServiceProvider();

        var validator = new TrainChainStartupValidator(
            discovery,
            provider.GetRequiredService<IServiceScopeFactory>(),
            new MediatorConfiguration()
        );

        await validator.StartingAsync(CancellationToken.None);
        await validator.StartAsync(CancellationToken.None);

        discovery.Received(1).DiscoverTrains();
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
    /// Hands the validator scopes whose container answers "is this a service?" through
    /// <paramref name="isService"/>, or, when that is null, does not offer the question at all.
    /// </summary>
    private sealed class ReplacedIsServiceScopeFactory(
        IServiceScopeFactory inner,
        IServiceProviderIsService? isService
    ) : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new Scope(inner.CreateScope(), isService);

        private sealed class Scope(IServiceScope inner, IServiceProviderIsService? isService)
            : IServiceScope
        {
            public IServiceProvider ServiceProvider { get; } =
                new Provider(inner.ServiceProvider, isService);

            public void Dispose() => inner.Dispose();
        }

        private sealed class Provider(IServiceProvider inner, IServiceProviderIsService? isService)
            : IServiceProvider
        {
            public object? GetService(Type serviceType) =>
                serviceType == typeof(IServiceProviderIsService)
                    ? isService
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

    public interface IAsyncOnlyService;

    /// <summary>
    /// Implements <see cref="IAsyncDisposable"/> and not <see cref="IDisposable"/>, which a
    /// scoped EF context or an HTTP-based client commonly does. Disposing a scope holding one
    /// synchronously throws, which is the failure the train below provokes.
    /// </summary>
    private sealed class AsyncOnlyService : IAsyncOnlyService, IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    public interface IAsyncDisposingTrain : IServiceTrain<ChainProbeInput, bool>;

    public class AsyncDisposingTrain(IAsyncOnlyService asyncOnly)
        : ServiceTrain<ChainProbeInput, bool>,
            IAsyncDisposingTrain
    {
        public IAsyncOnlyService AsyncOnly { get; } = asyncOnly;

        // The chain itself is fine. Only the scope the check builds it in is at issue.
        protected override Task<Either<Exception, bool>> Junctions() =>
            Chain<TextToNumber>().Chain<NumberToFlag>().Resolve();
    }

    public interface IUnregisteredProbeService;

    public interface INeedsUnregisteredTrain : IServiceTrain<ChainProbeInput, bool>;

    public class NeedsUnregisteredTrain(IUnregisteredProbeService probe)
        : ServiceTrain<ChainProbeInput, bool>,
            INeedsUnregisteredTrain
    {
        public IUnregisteredProbeService Probe { get; } = probe;

        protected override Task<Either<Exception, bool>> Junctions() =>
            Chain<TextToNumber>().Chain<NumberToFlag>().Resolve();
    }

    /// <summary>A class a chain names although it does not implement <c>IJunction</c>.</summary>
    public class NotAJunction;

    public interface INotAJunctionTrain : IServiceTrain<ChainProbeInput, bool>;

    public class NotAJunctionTrain : ServiceTrain<ChainProbeInput, bool>, INotAJunctionTrain
    {
        protected override Task<Either<Exception, bool>> Junctions() =>
            Chain<NotAJunction>().Resolve();
    }

    public interface ISeveralFaultsTrain : IServiceTrain<ChainProbeInput, bool>;

    public class SeveralFaultsTrain : ServiceTrain<ChainProbeInput, bool>, ISeveralFaultsTrain
    {
        // NotAJunction is refused and produces nothing, so NumberToFlag, written second, needs an
        // int that nothing produces.
        protected override Task<Either<Exception, bool>> Junctions() =>
            Chain<NotAJunction>().Chain<NumberToFlag>().Resolve();
    }

    public class TwoConstructorJunction : Junction<ChainProbeInput, bool>
    {
        public TwoConstructorJunction() { }

        public TwoConstructorJunction(int ignored) { }

        public override Task<bool> Run(ChainProbeInput input) => Task.FromResult(true);
    }

    public interface ITwoConstructorJunctionTrain : IServiceTrain<ChainProbeInput, bool>;

    public class TwoConstructorJunctionTrain
        : ServiceTrain<ChainProbeInput, bool>,
            ITwoConstructorJunctionTrain
    {
        protected override Task<Either<Exception, bool>> Junctions() =>
            Chain<TwoConstructorJunction>().Resolve();
    }

    public interface IGenericProbe<T>;

    /// <summary>Holds a type whose own name carries no arity marker although it is generic.</summary>
    public class GenericProbeHolder<T>
    {
        public interface INestedProbe;
    }

    public interface INeedsSeveralUnregisteredTrain : IServiceTrain<ChainProbeInput, bool>;

    public class NeedsSeveralUnregisteredTrain(
        IUnregisteredProbeService probe,
        IGenericProbe<int> generic,
        GenericProbeHolder<string>.INestedProbe nested
    ) : ServiceTrain<ChainProbeInput, bool>, INeedsSeveralUnregisteredTrain
    {
        public object[] Probes { get; } = [probe, generic, nested];

        protected override Task<Either<Exception, bool>> Junctions() =>
            Chain<TextToNumber>().Chain<NumberToFlag>().Resolve();
    }

    public interface IKeyedProbe;

    public interface IOptionalArgumentsTrain : IServiceTrain<ChainProbeInput, bool>;

    /// <summary>
    /// Registered as it is, it cannot be built: the container has no keyed IKeyedProbe. Every
    /// argument it names is either defaulted or keyed, so none of them is reported missing.
    /// </summary>
    public class OptionalArgumentsTrain(
        [FromKeyedServices("probe")] IKeyedProbe keyed,
        [ServiceKey] object? key = null,
        IUnregisteredProbeService? optional = null
    ) : ServiceTrain<ChainProbeInput, bool>, IOptionalArgumentsTrain
    {
        public object?[] Arguments { get; } = [keyed, key, optional];

        protected override Task<Either<Exception, bool>> Junctions() =>
            Chain<TextToNumber>().Chain<NumberToFlag>().Resolve();
    }

    public interface INoResultTrain : IServiceTrain<ChainProbeInput, bool>;

    public class NoResultTrain : ServiceTrain<ChainProbeInput, bool>, INoResultTrain
    {
        // TextToNumber leaves an int in Memory, and the train returns a bool.
        protected override Task<Either<Exception, bool>> Junctions() =>
            Chain<TextToNumber>().Resolve();
    }

    public interface IWideInputTrain
        : IServiceTrain<(int, int, int, int, int, int, int, int), bool>;

    /// <summary>Takes eight values as one tuple, one more than Memory can store.</summary>
    public class WideInputTrain
        : ServiceTrain<(int, int, int, int, int, int, int, int), bool>,
            IWideInputTrain
    {
        protected override Task<Either<Exception, bool>> Junctions() =>
            Chain<WideToFlag>().Resolve();
    }

    private class WideToFlag : Junction<(int, int, int, int, int, int, int, int), bool>
    {
        public override Task<bool> Run((int, int, int, int, int, int, int, int) input) =>
            Task.FromResult(input.Item8 > 0);
    }

    public interface IRefusalAndNoResultTrain : IServiceTrain<ChainProbeInput, bool>;

    public class RefusalAndNoResultTrain
        : ServiceTrain<ChainProbeInput, bool>,
            IRefusalAndNoResultTrain
    {
        // IChain names a class, which is refused, but the step still records TextToNumber's int.
        // Nothing produces the bool the train returns, whatever happens to the refusal.
        protected override Task<Either<Exception, bool>> Junctions() =>
            IChain<TextToNumber>().Resolve();
    }

    public class NeedsProbeJunction(IUnregisteredProbeService probe)
        : Junction<ChainProbeInput, bool>
    {
        public IUnregisteredProbeService Probe { get; } = probe;

        public override Task<bool> Run(ChainProbeInput input) => Task.FromResult(true);
    }

    public interface IJunctionNeedsUnregisteredTrain : IServiceTrain<ChainProbeInput, bool>;

    public class JunctionNeedsUnregisteredTrain
        : ServiceTrain<ChainProbeInput, bool>,
            IJunctionNeedsUnregisteredTrain
    {
        protected override Task<Either<Exception, bool>> Junctions() =>
            Chain<NeedsProbeJunction>().Resolve();
    }

    public interface IProbeRepository;

    public class ProbeRepository(IUnregisteredProbeService probe) : IProbeRepository
    {
        public IUnregisteredProbeService Probe { get; } = probe;
    }

    public interface INeedsNestedUnregisteredTrain : IServiceTrain<ChainProbeInput, bool>;

    /// <summary>Registered as it is, its one argument is registered and cannot be built.</summary>
    public class NeedsNestedUnregisteredTrain(IProbeRepository repository)
        : ServiceTrain<ChainProbeInput, bool>,
            INeedsNestedUnregisteredTrain
    {
        public IProbeRepository Repository { get; } = repository;

        protected override Task<Either<Exception, bool>> Junctions() =>
            Chain<TextToNumber>().Chain<NumberToFlag>().Resolve();
    }

    public interface INoPublicConstructorTrain : IServiceTrain<ChainProbeInput, bool>;

    public class NoPublicConstructorTrain
        : ServiceTrain<ChainProbeInput, bool>,
            INoPublicConstructorTrain
    {
        private NoPublicConstructorTrain() { }

        protected override Task<Either<Exception, bool>> Junctions() =>
            Chain<TextToNumber>().Chain<NumberToFlag>().Resolve();
    }

    public class UnregisteredProbeService : IUnregisteredProbeService;

    public interface IInjectsUnregisteredTrain : IServiceTrain<ChainProbeInput, bool>;

    public class InjectsUnregisteredTrain
        : ServiceTrain<ChainProbeInput, bool>,
            IInjectsUnregisteredTrain
    {
        [Inject]
        public IUnregisteredProbeService Probe { get; set; } = null!;

        protected override Task<Either<Exception, bool>> Junctions() =>
            Chain<TextToNumber>().Chain<NumberToFlag>().Resolve();
    }

    public interface IOptionallyInjectsTrain : IServiceTrain<ChainProbeInput, bool>;

    public class OptionallyInjectsTrain
        : ServiceTrain<ChainProbeInput, bool>,
            IOptionallyInjectsTrain
    {
        [Inject]
        public IUnregisteredProbeService? Probe { get; set; }

        protected override Task<Either<Exception, bool>> Junctions() =>
            Chain<TextToNumber>().Chain<NumberToFlag>().Resolve();
    }

    public interface IOptionallyInjectsRepositoryTrain : IServiceTrain<ChainProbeInput, bool>;

    public class OptionallyInjectsRepositoryTrain
        : ServiceTrain<ChainProbeInput, bool>,
            IOptionallyInjectsRepositoryTrain
    {
        [Inject]
        public IProbeRepository? Repository { get; set; }

        protected override Task<Either<Exception, bool>> Junctions() =>
            Chain<TextToNumber>().Chain<NumberToFlag>().Resolve();
    }

    /// <summary>Built by its constructor in the chain, so its property is never filled.</summary>
    public class InjectingJunction : Junction<ChainProbeInput, int>
    {
        [Inject]
        public IUnregisteredProbeService? Probe { get; set; }

        public override Task<int> Run(ChainProbeInput input) => Task.FromResult(input.Value.Length);
    }

    public interface IChainsInjectingJunctionTrain : IServiceTrain<ChainProbeInput, bool>;

    public class ChainsInjectingJunctionTrain
        : ServiceTrain<ChainProbeInput, bool>,
            IChainsInjectingJunctionTrain
    {
        protected override Task<Either<Exception, bool>> Junctions() =>
            Chain<InjectingJunction>().Chain<NumberToFlag>().Resolve();
    }

    public interface IProbeClock;

    public class ProbeClock : IProbeClock;

    public interface IInitializedInjectTrain : IServiceTrain<ChainProbeInput, bool>;

    public class InitializedInjectTrain
        : ServiceTrain<ChainProbeInput, bool>,
            IInitializedInjectTrain
    {
        [Inject]
        public IProbeClock Clock { get; set; } = new ProbeClock();

        protected override Task<Either<Exception, bool>> Junctions() =>
            Chain<TextToNumber>().Chain<NumberToFlag>().Resolve();
    }

    public interface IConstructorSetInjectTrain : IServiceTrain<ChainProbeInput, bool>;

    public class ConstructorSetInjectTrain
        : ServiceTrain<ChainProbeInput, bool>,
            IConstructorSetInjectTrain
    {
        public ConstructorSetInjectTrain()
        {
            Clock = new ProbeClock();
        }

        [Inject]
        public IProbeClock Clock { get; set; }

        protected override Task<Either<Exception, bool>> Junctions() =>
            Chain<TextToNumber>().Chain<NumberToFlag>().Resolve();
    }

    public class StandInRepository : IProbeRepository;

    public interface IInitializedRepositoryTrain : IServiceTrain<ChainProbeInput, bool>;

    public class InitializedRepositoryTrain
        : ServiceTrain<ChainProbeInput, bool>,
            IInitializedRepositoryTrain
    {
        [Inject]
        public IProbeRepository Repository { get; set; } = new StandInRepository();

        protected override Task<Either<Exception, bool>> Junctions() =>
            Chain<TextToNumber>().Chain<NumberToFlag>().Resolve();
    }

    public interface IFillFailsTrain : IServiceTrain<ChainProbeInput, bool>;

    public class FillFailsTrain : ServiceTrain<ChainProbeInput, bool>, IFillFailsTrain
    {
        [Inject]
        public IProbeClock Clock { get; set; } = new ProbeClock();

        [Inject]
        public IProbeRepository? Repository { get; set; }

        protected override Task<Either<Exception, bool>> Junctions() =>
            Chain<TextToNumber>().Chain<NumberToFlag>().Resolve();
    }

    public interface IRequestBoundInjectingTrain : IServiceTrain<ChainProbeInput, bool>;

    public class RequestBoundInjectingTrain(IRequestOnlyService requestOnly)
        : ServiceTrain<ChainProbeInput, bool>,
            IRequestBoundInjectingTrain
    {
        public IRequestOnlyService RequestOnly { get; } = requestOnly;

        [Inject]
        public IUnregisteredProbeService Probe { get; set; } = null!;

        protected override Task<Either<Exception, bool>> Junctions() =>
            Chain<TextToNumber>().Chain<NumberToFlag>().Resolve();
    }

    public interface IObliviousInjectTrain : IServiceTrain<ChainProbeInput, bool>;

#nullable disable
    public class ObliviousInjectTrain : ServiceTrain<ChainProbeInput, bool>, IObliviousInjectTrain
    {
        [Inject]
        public IUnregisteredProbeService Probe { get; set; }

        protected override Task<Either<Exception, bool>> Junctions() =>
            Chain<TextToNumber>().Chain<NumberToFlag>().Resolve();
    }
#nullable restore
}
