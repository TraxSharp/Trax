using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Mediator.Configuration;
using Trax.Mediator.Exceptions;
using Trax.Mediator.Services.ConcurrencyLimiter;
using Trax.Mediator.Services.RunExecutor;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.UnitTests;

[TestFixture]
public class TrainExecutionServiceLookupTests
{
    private record EmptyIn;

    private record EmptyOut;

    private static TrainRegistration FakeRegistration(
        string fullName,
        string serviceTypeName,
        Type? serviceType = null,
        bool hasAuthorize = false
    )
    {
        return new TrainRegistration
        {
            ServiceType = serviceType ?? typeof(IFakeTrain),
            ImplementationType = typeof(FakeTrainImpl),
            InputType = typeof(EmptyIn),
            OutputType = typeof(EmptyOut),
            Lifetime = ServiceLifetime.Transient,
            ServiceTypeName = serviceTypeName,
            ImplementationTypeName = "FakeImpl",
            InputTypeName = "EmptyIn",
            OutputTypeName = "EmptyOut",
            RequiredPolicies = [],
            RequiredRoles = [],
            HasAuthorizeAttribute = hasAuthorize,
            IsQuery = false,
            IsMutation = false,
            IsBroadcastEnabled = false,
            IsRemote = false,
            GraphQLOperations = Effect.Attributes.GraphQLOperation.Run,
        };
    }

    private static TrainExecutionService BuildService(
        MediatorConfiguration? config = null,
        params TrainRegistration[] trains
    )
    {
        var discovery = Substitute.For<ITrainDiscoveryService>();
        discovery.DiscoverTrains().Returns(trains);

        var runExecutor = Substitute.For<IRunExecutor>();
        var limiter = Substitute.For<IConcurrencyLimiter>();
        var dataCtxFactory = Substitute.For<IDataContextProviderFactory>();
        var services = new ServiceCollection();
        return new TrainExecutionService(
            discovery,
            runExecutor,
            limiter,
            dataCtxFactory,
            config ?? new MediatorConfiguration(),
            services.BuildServiceProvider()
        );
    }

    private static TrainExecutionService BuildService(params TrainRegistration[] trains) =>
        BuildService(null, trains);

    // Use reflection to exercise the private FindTrain method directly so these
    // tests don't need a full run harness.
    private static object InvokeFindTrain(TrainExecutionService svc, string name)
    {
        var method = typeof(TrainExecutionService).GetMethod(
            "FindTrain",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
        );
        try
        {
            return method!.Invoke(svc, [name])!;
        }
        catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    [Test]
    public void FindTrain_ByFullName_Succeeds()
    {
        var reg = FakeRegistration("Ns.Foo.IMyTrain", "IMyTrain");
        var svc = BuildService(reg);

        var result = InvokeFindTrain(svc, typeof(IFakeTrain).FullName!);

        result.Should().BeSameAs(reg);
    }

    [Test]
    public void FindTrain_ByFriendlyName_Succeeds_WhenUnique()
    {
        var reg = FakeRegistration("Ns.Foo.IMyTrain", "Ns.Foo.IMyTrain");
        var svc = BuildService(reg);

        var result = InvokeFindTrain(svc, "Ns.Foo.IMyTrain");

        result.Should().BeSameAs(reg);
    }

    [Test]
    public void FindTrain_AmbiguousFriendlyName_Throws()
    {
        var regA = FakeRegistration("Ns.A.IMyTrain", "Ns.A.IMyTrain", typeof(IFakeTrain));
        var regB = FakeRegistration("Ns.B.IMyTrain", "Ns.A.IMyTrain", typeof(IFakeTrainTwo));
        var svc = BuildService(regA, regB);

        var act = () => InvokeFindTrain(svc, "Ns.A.IMyTrain");

        act.Should().Throw<AmbiguousTrainNameException>();
    }

    [Test]
    public void FindTrain_ByShortName_Throws()
    {
        // Regression: the old implementation fell back to ServiceType.Name, which
        // silently collided across namespaces. The short-name fallback has been
        // removed; callers must pass the interface FullName.
        var reg = FakeRegistration("Ns.Foo.IMyTrain", "Ns.Foo.IMyTrain");
        var svc = BuildService(reg);

        var act = () => InvokeFindTrain(svc, typeof(IFakeTrain).Name);

        act.Should().Throw<TrainNotFoundException>();
    }

    [Test]
    public void FindTrain_UnknownName_ThrowsTrainNotFound_WithGenericMessage()
    {
        var svc = BuildService();

        var act = () => InvokeFindTrain(svc, "Does.Not.Exist");

        act.Should()
            .Throw<TrainNotFoundException>()
            .Where(ex => !ex.Message.Contains("Does.Not.Exist"));
    }

    [Test]
    public async Task RunAsync_InputOverCap_Throws_TrainInputValidationException()
    {
        var reg = FakeRegistration(
            typeof(IFakeTrain).FullName!,
            typeof(IFakeTrain).FullName!,
            typeof(IFakeTrain)
        );
        var config = new MediatorConfiguration { MaxInputJsonBytes = 32 };
        var svc = BuildService(config, reg);
        var oversized = "{\"pad\":\"" + new string('x', 128) + "\"}";

        var act = async () => await svc.RunAsync(typeof(IFakeTrain).FullName!, oversized);

        (await act.Should().ThrowAsync<TrainInputValidationException>())
            .Which.MaxBytes.Should()
            .Be(32);
    }

    [Test]
    public async Task QueueAsync_InputOverCap_Throws_TrainInputValidationException()
    {
        var reg = FakeRegistration(
            typeof(IFakeTrain).FullName!,
            typeof(IFakeTrain).FullName!,
            typeof(IFakeTrain)
        );
        var config = new MediatorConfiguration { MaxInputJsonBytes = 16 };
        var svc = BuildService(config, reg);
        var oversized = "{\"pad\":\"" + new string('x', 64) + "\"}";

        var act = async () => await svc.QueueAsync(typeof(IFakeTrain).FullName!, oversized);

        await act.Should().ThrowAsync<TrainInputValidationException>();
    }

    [Test]
    public async Task RunAsync_InputAtCap_DoesNotThrowForSize()
    {
        // We don't wire up a run executor result in this lightweight harness — the
        // call will fail later for a different reason. We just need to confirm the
        // size check does NOT fire on a byte-count equal to the cap.
        var reg = FakeRegistration(
            typeof(IFakeTrain).FullName!,
            typeof(IFakeTrain).FullName!,
            typeof(IFakeTrain)
        );
        var json = "{\"pad\":\"ok\"}"; // 12 UTF-8 bytes
        var config = new MediatorConfiguration { MaxInputJsonBytes = 12 };
        var svc = BuildService(config, reg);

        var act = async () => await svc.RunAsync(typeof(IFakeTrain).FullName!, json);

        // Any thrown exception must not be the size-check one.
        try
        {
            await act();
        }
        catch (TrainInputValidationException)
        {
            Assert.Fail("Input at the exact cap was wrongly rejected by the size check.");
        }
        catch
        {
            // Expected: downstream failure (deserializer / executor). Not our concern.
        }
    }

    [Test]
    public async Task RunAsync_InputOverCap_RunsAfter_Authorize()
    {
        // The size check must run after authorization so unauthenticated probing
        // cannot discover the cap value. Here we prove order by wiring an authz
        // service that always throws; the thrown exception must be the authz one,
        // not the size one, even though the input is both oversized and unauthorized.
        var reg = FakeRegistration(
            typeof(IFakeTrain).FullName!,
            typeof(IFakeTrain).FullName!,
            typeof(IFakeTrain),
            hasAuthorize: true
        );

        var authService =
            Substitute.For<Mediator.Services.TrainAuthorization.ITrainAuthorizationService>();
        authService
            .AuthorizeAsync(Arg.Any<TrainRegistration>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("authz-first")));

        var discovery = Substitute.For<ITrainDiscoveryService>();
        discovery.DiscoverTrains().Returns([reg]);
        var runExecutor = Substitute.For<IRunExecutor>();
        var limiter = Substitute.For<IConcurrencyLimiter>();
        var dataCtxFactory = Substitute.For<IDataContextProviderFactory>();
        var services = new ServiceCollection();
        services.AddSingleton(authService);
        var config = new MediatorConfiguration { MaxInputJsonBytes = 8 };

        var svc = new TrainExecutionService(
            discovery,
            runExecutor,
            limiter,
            dataCtxFactory,
            config,
            services.BuildServiceProvider()
        );

        var oversized = "{\"big\":\"" + new string('x', 256) + "\"}";
        var act = async () => await svc.RunAsync(typeof(IFakeTrain).FullName!, oversized);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should()
            .Be("authz-first");
    }

    // Fakes used by the tests above
    public interface IFakeTrain;

    public interface IFakeTrainTwo;

    public class FakeTrainImpl : IFakeTrain, IFakeTrainTwo;
}
