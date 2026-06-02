using FluentAssertions;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.UnitTests;

[TestFixture]
public class TrainDiscoveryAllowAnonymousTests
{
    private static TrainRegistration Discover<TService, TImpl>()
        where TImpl : class, TService
        where TService : class
    {
        var services = new ServiceCollection();
        services.AddScoped<TService, TImpl>();
        services.AddScoped<TImpl>();
        var discovery = new TrainDiscoveryService(services);
        return discovery
            .DiscoverTrains()
            .Single(r =>
                r.ServiceType == typeof(TService) || r.ImplementationType == typeof(TImpl)
            );
    }

    [Test]
    public void AllowAnonymous_OnImplementation_IsDiscovered()
    {
        var reg = Discover<IAnonImplTrain, AnonImplTrain>();

        reg.HasAllowAnonymousAttribute.Should().BeTrue();
        reg.HasAuthorizeAttribute.Should().BeFalse();
    }

    [Test]
    public void AllowAnonymous_OnInterface_IsDiscovered()
    {
        var reg = Discover<IAnonInterfaceTrain, AnonInterfaceTrainImpl>();

        reg.HasAllowAnonymousAttribute.Should().BeTrue();
    }

    [Test]
    public void AllowAnonymous_OnBaseClass_IsDiscovered()
    {
        var reg = Discover<IAnonBaseTrain, AnonBaseTrainImpl>();

        reg.HasAllowAnonymousAttribute.Should().BeTrue();
    }

    [Test]
    public void NoAllowAnonymous_Anywhere_IsDiscoveredAsFalse()
    {
        var reg = Discover<IPlainAnonTrain, PlainAnonTrainImpl>();

        reg.HasAllowAnonymousAttribute.Should().BeFalse();
    }

    [Test]
    public void BothAttributes_DiscoverySetsBothFlags_WithoutThrowing()
    {
        // Discovery is permissive: the conflict between [TraxAuthorize] and
        // [TraxAllowAnonymous] is surfaced by exposure validation at host startup,
        // not here. Discovery must report both flags so that validator can see them.
        var reg = Discover<IConflictTrain, ConflictTrainImpl>();

        reg.HasAuthorizeAttribute.Should().BeTrue();
        reg.HasAllowAnonymousAttribute.Should().BeTrue();
    }

    // ─── Fakes ───────────────────────────────────────────────

    public record EmptyIn;

    public record EmptyOut;

    public interface IAnonImplTrain : IServiceTrain<EmptyIn, EmptyOut>;

    [TraxAllowAnonymous]
    public class AnonImplTrain : ServiceTrain<EmptyIn, EmptyOut>, IAnonImplTrain
    {
        protected override Task<Either<Exception, EmptyOut>> RunInternal(EmptyIn input) =>
            Task.FromResult<Either<Exception, EmptyOut>>(new EmptyOut());
    }

    [TraxAllowAnonymous]
    public interface IAnonInterfaceTrain : IServiceTrain<EmptyIn, EmptyOut>;

    public class AnonInterfaceTrainImpl : ServiceTrain<EmptyIn, EmptyOut>, IAnonInterfaceTrain
    {
        protected override Task<Either<Exception, EmptyOut>> RunInternal(EmptyIn input) =>
            Task.FromResult<Either<Exception, EmptyOut>>(new EmptyOut());
    }

    public interface IAnonBaseTrain : IServiceTrain<EmptyIn, EmptyOut>;

    [TraxAllowAnonymous]
    public abstract class AnonBase : ServiceTrain<EmptyIn, EmptyOut>
    {
        protected override Task<Either<Exception, EmptyOut>> RunInternal(EmptyIn input) =>
            Task.FromResult<Either<Exception, EmptyOut>>(new EmptyOut());
    }

    public class AnonBaseTrainImpl : AnonBase, IAnonBaseTrain { }

    public interface IPlainAnonTrain : IServiceTrain<EmptyIn, EmptyOut>;

    public class PlainAnonTrainImpl : ServiceTrain<EmptyIn, EmptyOut>, IPlainAnonTrain
    {
        protected override Task<Either<Exception, EmptyOut>> RunInternal(EmptyIn input) =>
            Task.FromResult<Either<Exception, EmptyOut>>(new EmptyOut());
    }

    public interface IConflictTrain : IServiceTrain<EmptyIn, EmptyOut>;

    [TraxAuthorize("Admin")]
    [TraxAllowAnonymous]
    public class ConflictTrainImpl : ServiceTrain<EmptyIn, EmptyOut>, IConflictTrain
    {
        protected override Task<Either<Exception, EmptyOut>> RunInternal(EmptyIn input) =>
            Task.FromResult<Either<Exception, EmptyOut>>(new EmptyOut());
    }
}
