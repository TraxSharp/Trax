using FluentAssertions;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.UnitTests;

/// <summary>
/// Discovery reads <c>[TraxAuthorize]</c> from every surface a train can carry it on, and keeps
/// the declared roles exactly as written.
///
/// <para>Enforces Trax.Docs/adr/0026-train-roles-match-exactly-like-authorize.md.</para>
/// </summary>
[Property("adr", "Trax.Docs/adr/0026-train-roles-match-exactly-like-authorize.md")]
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
        reg.RequiredRoles.Should().Contain("Manager");
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
        reg.RequiredRoles.Should().BeEquivalentTo(new[] { "RoleA", "RoleB" });
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
    public void Roles_AreKept_AsDeclared()
    {
        var reg = Discover<IMixedCaseRolesTrain, MixedCaseRolesTrainImpl>();

        reg.RequiredRoles.Should()
            .Equal(
                ["admin", "Manager", "AUDITOR"],
                "train roles match a principal's role claims exactly and ordinally, like "
                    + "@authorize, so discovery must not fold their case "
                    + "(Trax.Docs/adr/0026-train-roles-match-exactly-like-authorize.md)"
            );
    }

    [Test]
    public void Roles_AreNotCaseMapped_SoLookalikesStayDistinct()
    {
        var reg = Discover<ILookalikeRolesTrain, LookalikeRolesTrainImpl>();

        // Upper-casing with the invariant culture turned the long s into S, so a claim of
        // "SUPERUSER" satisfied a role declared as "\u017Fuperuser".
        reg.RequiredRoles.Should()
            .Equal(
                ["\u017Fuperuser", "Admin", "admin"],
                "a role that only resembles another under case mapping is a different role "
                    + "(Trax.Docs/adr/0026-train-roles-match-exactly-like-authorize.md)"
            );
    }

    // The startup-time validation for malformed attribute shapes
    // (whitespace-only Roles, empty Policy) is exercised in
    // AuthorizationRegistrationValidatorTests. Keeping intentionally-malformed
    // IServiceTrain types in this shared test assembly would pollute every
    // assembly-scan-based integration test, so those fixtures live inside the
    // validator's test file and are registered manually.

    [TraxAuthorize(Roles = "\u017Fuperuser, Admin, admin")]
    public interface ILookalikeRolesTrain : IServiceTrain<EmptyIn, EmptyOut>;

    public class LookalikeRolesTrainImpl : ServiceTrain<EmptyIn, EmptyOut>, ILookalikeRolesTrain
    {
        protected override Task<Either<Exception, EmptyOut>> Junctions() =>
            Task.FromResult<Either<Exception, EmptyOut>>(new EmptyOut());
    }

    [TraxAuthorize(Roles = "admin, Manager, AUDITOR")]
    public interface IMixedCaseRolesTrain : IServiceTrain<EmptyIn, EmptyOut>;

    public class MixedCaseRolesTrainImpl : ServiceTrain<EmptyIn, EmptyOut>, IMixedCaseRolesTrain
    {
        protected override Task<Either<Exception, EmptyOut>> Junctions() =>
            Task.FromResult<Either<Exception, EmptyOut>>(new EmptyOut());
    }

    // ─── Fakes ───────────────────────────────────────────────

    public record EmptyIn;

    public record EmptyOut;

    public interface IPlainTrain : IServiceTrain<EmptyIn, EmptyOut>;

    [TraxAuthorize("Admin")]
    public class ImplementationAuthorizedTrain : ServiceTrain<EmptyIn, EmptyOut>, IPlainTrain
    {
        protected override Task<Either<Exception, EmptyOut>> Junctions() =>
            Task.FromResult<Either<Exception, EmptyOut>>(new EmptyOut());
    }

    [TraxAuthorize(Roles = "Manager")]
    public interface IInterfaceAuthorizedTrain : IServiceTrain<EmptyIn, EmptyOut>;

    public class InterfaceAuthorizedTrainImpl
        : ServiceTrain<EmptyIn, EmptyOut>,
            IInterfaceAuthorizedTrain
    {
        protected override Task<Either<Exception, EmptyOut>> Junctions() =>
            Task.FromResult<Either<Exception, EmptyOut>>(new EmptyOut());
    }

    public interface IBaseAuthorizedTrain : IServiceTrain<EmptyIn, EmptyOut>;

    [TraxAuthorize("BasePolicy")]
    public abstract class AuthorizedBase : ServiceTrain<EmptyIn, EmptyOut>
    {
        protected override Task<Either<Exception, EmptyOut>> Junctions() =>
            Task.FromResult<Either<Exception, EmptyOut>>(new EmptyOut());
    }

    public class BaseAuthorizedTrainImpl : AuthorizedBase, IBaseAuthorizedTrain { }

    [TraxAuthorize("FromInterface", Roles = "RoleA")]
    public interface IMultiSurfaceTrain : IServiceTrain<EmptyIn, EmptyOut>;

    [TraxAuthorize("FromImpl", Roles = "RoleB")]
    public class MultiSurfaceTrainImpl : ServiceTrain<EmptyIn, EmptyOut>, IMultiSurfaceTrain
    {
        protected override Task<Either<Exception, EmptyOut>> Junctions() =>
            Task.FromResult<Either<Exception, EmptyOut>>(new EmptyOut());
    }

    public interface IUnauthTrain : IServiceTrain<EmptyIn, EmptyOut>;

    public class UnauthTrainImpl : ServiceTrain<EmptyIn, EmptyOut>, IUnauthTrain
    {
        protected override Task<Either<Exception, EmptyOut>> Junctions() =>
            Task.FromResult<Either<Exception, EmptyOut>>(new EmptyOut());
    }
}
