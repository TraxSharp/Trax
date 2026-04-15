using FluentAssertions;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.UnitTests;

[TestFixture]
public class TrainDiscoveryAuthorizationTests
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
    public void Attribute_OnImplementation_IsDiscovered()
    {
        var reg = Discover<IPlainTrain, ImplementationAuthorizedTrain>();

        reg.HasAuthorizeAttribute.Should().BeTrue();
        reg.RequiredPolicies.Should().Contain("Admin");
    }

    [Test]
    public void Attribute_OnInterface_IsDiscovered()
    {
        var reg = Discover<IInterfaceAuthorizedTrain, InterfaceAuthorizedTrainImpl>();

        reg.HasAuthorizeAttribute.Should().BeTrue();
        reg.RequiredRoles.Should().Contain("MANAGER");
    }

    [Test]
    public void Attribute_OnBaseClass_IsDiscovered()
    {
        var reg = Discover<IBaseAuthorizedTrain, BaseAuthorizedTrainImpl>();

        reg.HasAuthorizeAttribute.Should().BeTrue();
        reg.RequiredPolicies.Should().Contain("BasePolicy");
    }

    [Test]
    public void Attribute_FromInterfaceAndImpl_AreUnioned()
    {
        var reg = Discover<IMultiSurfaceTrain, MultiSurfaceTrainImpl>();

        reg.HasAuthorizeAttribute.Should().BeTrue();
        reg.RequiredPolicies.Should().BeEquivalentTo(new[] { "FromInterface", "FromImpl" });
        // Roles are normalized to upper-invariant at discovery so comparisons
        // downstream are case-insensitive.
        reg.RequiredRoles.Should().BeEquivalentTo(new[] { "ROLEA", "ROLEB" });
    }

    [Test]
    public void NoAttribute_Anywhere_IsDiscoveredAsUnauthorized()
    {
        var reg = Discover<IUnauthTrain, UnauthTrainImpl>();

        reg.HasAuthorizeAttribute.Should().BeFalse();
        reg.RequiredPolicies.Should().BeEmpty();
        reg.RequiredRoles.Should().BeEmpty();
    }

    [Test]
    public void Roles_AreNormalized_ToUpperInvariant()
    {
        var reg = Discover<IMixedCaseRolesTrain, MixedCaseRolesTrainImpl>();

        reg.RequiredRoles.Should().BeEquivalentTo(new[] { "ADMIN", "MANAGER", "AUDITOR" });
    }

    // The startup-time validation for malformed attribute shapes
    // (whitespace-only Roles, empty Policy) is exercised in
    // AuthorizationRegistrationValidatorTests. Keeping intentionally-malformed
    // IServiceTrain types in this shared test assembly would pollute every
    // assembly-scan-based integration test, so those fixtures live inside the
    // validator's test file and are registered manually.

    [TraxAuthorize(Roles = "admin, Manager, AUDITOR")]
    public interface IMixedCaseRolesTrain : IServiceTrain<EmptyIn, EmptyOut>;

    public class MixedCaseRolesTrainImpl : ServiceTrain<EmptyIn, EmptyOut>, IMixedCaseRolesTrain
    {
        protected override Task<Either<Exception, EmptyOut>> RunInternal(EmptyIn input) =>
            Task.FromResult<Either<Exception, EmptyOut>>(new EmptyOut());
    }

    // ─── Fakes ───────────────────────────────────────────────

    public record EmptyIn;

    public record EmptyOut;

    public interface IPlainTrain : IServiceTrain<EmptyIn, EmptyOut>;

    [TraxAuthorize("Admin")]
    public class ImplementationAuthorizedTrain : ServiceTrain<EmptyIn, EmptyOut>, IPlainTrain
    {
        protected override Task<Either<Exception, EmptyOut>> RunInternal(EmptyIn input) =>
            Task.FromResult<Either<Exception, EmptyOut>>(new EmptyOut());
    }

    [TraxAuthorize(Roles = "Manager")]
    public interface IInterfaceAuthorizedTrain : IServiceTrain<EmptyIn, EmptyOut>;

    public class InterfaceAuthorizedTrainImpl
        : ServiceTrain<EmptyIn, EmptyOut>,
            IInterfaceAuthorizedTrain
    {
        protected override Task<Either<Exception, EmptyOut>> RunInternal(EmptyIn input) =>
            Task.FromResult<Either<Exception, EmptyOut>>(new EmptyOut());
    }

    public interface IBaseAuthorizedTrain : IServiceTrain<EmptyIn, EmptyOut>;

    [TraxAuthorize("BasePolicy")]
    public abstract class AuthorizedBase : ServiceTrain<EmptyIn, EmptyOut>
    {
        protected override Task<Either<Exception, EmptyOut>> RunInternal(EmptyIn input) =>
            Task.FromResult<Either<Exception, EmptyOut>>(new EmptyOut());
    }

    public class BaseAuthorizedTrainImpl : AuthorizedBase, IBaseAuthorizedTrain { }

    [TraxAuthorize("FromInterface", Roles = "RoleA")]
    public interface IMultiSurfaceTrain : IServiceTrain<EmptyIn, EmptyOut>;

    [TraxAuthorize("FromImpl", Roles = "RoleB")]
    public class MultiSurfaceTrainImpl : ServiceTrain<EmptyIn, EmptyOut>, IMultiSurfaceTrain
    {
        protected override Task<Either<Exception, EmptyOut>> RunInternal(EmptyIn input) =>
            Task.FromResult<Either<Exception, EmptyOut>>(new EmptyOut());
    }

    public interface IUnauthTrain : IServiceTrain<EmptyIn, EmptyOut>;

    public class UnauthTrainImpl : ServiceTrain<EmptyIn, EmptyOut>, IUnauthTrain
    {
        protected override Task<Either<Exception, EmptyOut>> RunInternal(EmptyIn input) =>
            Task.FromResult<Either<Exception, EmptyOut>>(new EmptyOut());
    }
}
