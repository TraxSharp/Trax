using FluentAssertions;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Trax.Effect.Attributes;
using Trax.Effect.Extensions;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Configuration;
using Trax.Mediator.Services.TrainAuthorization;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.UnitTests;

[TestFixture]
public class AuthorizationRegistrationValidatorTests
{
    public record EmptyIn;

    public record EmptyOut;

    public interface ITestAuthedTrain : IServiceTrain<EmptyIn, EmptyOut>;

    [TraxAuthorize("Admin")]
    public class TestAuthedTrain : ServiceTrain<EmptyIn, EmptyOut>, ITestAuthedTrain
    {
        protected override Task<Either<Exception, EmptyOut>> RunInternal(EmptyIn input) =>
            Task.FromResult<Either<Exception, EmptyOut>>(new EmptyOut());
    }

    public interface IPlainTrain : IServiceTrain<EmptyIn, EmptyOut>;

    public class PlainTrain : ServiceTrain<EmptyIn, EmptyOut>, IPlainTrain
    {
        protected override Task<Either<Exception, EmptyOut>> RunInternal(EmptyIn input) =>
            Task.FromResult<Either<Exception, EmptyOut>>(new EmptyOut());
    }

    [Test]
    public async Task StartAsync_WhenAuthTrainRegistered_AndNoAuthServiceRegistered_Throws()
    {
        var services = new ServiceCollection();
        services.AddScopedTraxRoute<ITestAuthedTrain, TestAuthedTrain>();
        var discovery = new TrainDiscoveryService(services);
        var config = new MediatorConfiguration();
        var sp = services.BuildServiceProvider();

        var validator = new AuthorizationRegistrationValidator(discovery, config, sp);

        var act = async () => await validator.StartAsync(CancellationToken.None);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*no ITrainAuthorizationService is registered*");
    }

    [Test]
    public async Task StartAsync_WhenAuthTrainRegistered_AndAllowOptedIn_Allows()
    {
        var services = new ServiceCollection();
        services.AddScopedTraxRoute<ITestAuthedTrain, TestAuthedTrain>();
        var discovery = new TrainDiscoveryService(services);
        var config = new MediatorConfiguration { AllowMissingAuthorizationService = true };
        var sp = services.BuildServiceProvider();

        var validator = new AuthorizationRegistrationValidator(discovery, config, sp);

        await validator.StartAsync(CancellationToken.None);
    }

    [Test]
    public async Task StartAsync_WhenAuthServiceRegistered_Allows()
    {
        var services = new ServiceCollection();
        services.AddScopedTraxRoute<ITestAuthedTrain, TestAuthedTrain>();
        var authService = Substitute.For<ITrainAuthorizationService>();
        services.AddSingleton(authService);
        var discovery = new TrainDiscoveryService(services);
        var config = new MediatorConfiguration();
        var sp = services.BuildServiceProvider();

        var validator = new AuthorizationRegistrationValidator(discovery, config, sp);

        await validator.StartAsync(CancellationToken.None);
    }

    [Test]
    public async Task StartAsync_WhenAuthServiceScoped_AndScopeValidationOn_DoesNotThrow()
    {
        // Regression: the validator previously resolved ITrainAuthorizationService
        // off the root IServiceProvider. Once the real service is registered
        // Scoped (as it is in Trax.Api), ServiceProvider's scope validation
        // rejects the resolution with "Cannot resolve scoped service from
        // root provider" during hosted-service startup.
        var services = new ServiceCollection();
        services.AddScopedTraxRoute<ITestAuthedTrain, TestAuthedTrain>();
        services.AddScoped<ITrainAuthorizationService>(_ =>
            Substitute.For<ITrainAuthorizationService>()
        );
        var discovery = new TrainDiscoveryService(services);
        var config = new MediatorConfiguration();
        var sp = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true }
        );

        var validator = new AuthorizationRegistrationValidator(discovery, config, sp);

        await validator.StartAsync(CancellationToken.None);
    }

    [Test]
    public async Task StartAsync_WhenNoAuthorizedTrainsExist_Allows()
    {
        var services = new ServiceCollection();
        services.AddScopedTraxRoute<IPlainTrain, PlainTrain>();
        var discovery = new TrainDiscoveryService(services);
        var config = new MediatorConfiguration();
        var sp = services.BuildServiceProvider();

        var validator = new AuthorizationRegistrationValidator(discovery, config, sp);

        await validator.StartAsync(CancellationToken.None);
    }

    [Test]
    public async Task StartAsync_RolesWhitespaceOnly_Throws()
    {
        // Use a manual ServiceCollection rather than assembly-scan so the bad
        // fixture doesn't pollute other test assemblies. The validator walks
        // the attribute surface explicitly.
        var services = new ServiceCollection();
        services.AddScopedTraxRoute<IWhitespaceRolesTrain, WhitespaceRolesTrain>();
        var authService = Substitute.For<ITrainAuthorizationService>();
        services.AddSingleton(authService);
        var discovery = new TrainDiscoveryService(services);
        var config = new MediatorConfiguration();
        var sp = services.BuildServiceProvider();

        var validator = new AuthorizationRegistrationValidator(discovery, config, sp);

        var act = async () => await validator.StartAsync(CancellationToken.None);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*parsed to zero roles*");
    }

    [Test]
    public async Task StartAsync_EmptyPolicy_Throws()
    {
        var services = new ServiceCollection();
        services.AddScopedTraxRoute<IEmptyPolicyTrain, EmptyPolicyTrain>();
        var authService = Substitute.For<ITrainAuthorizationService>();
        services.AddSingleton(authService);
        var discovery = new TrainDiscoveryService(services);
        var config = new MediatorConfiguration();
        var sp = services.BuildServiceProvider();

        var validator = new AuthorizationRegistrationValidator(discovery, config, sp);

        var act = async () => await validator.StartAsync(CancellationToken.None);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*empty or whitespace Policy*");
    }

    [TraxAuthorize(Roles = ", ,  ")]
    public interface IWhitespaceRolesTrain : IServiceTrain<EmptyIn, EmptyOut>;

    public class WhitespaceRolesTrain : ServiceTrain<EmptyIn, EmptyOut>, IWhitespaceRolesTrain
    {
        protected override Task<Either<Exception, EmptyOut>> RunInternal(EmptyIn input) =>
            Task.FromResult<Either<Exception, EmptyOut>>(new EmptyOut());
    }

    [TraxAuthorize("")]
    public interface IEmptyPolicyTrain : IServiceTrain<EmptyIn, EmptyOut>;

    public class EmptyPolicyTrain : ServiceTrain<EmptyIn, EmptyOut>, IEmptyPolicyTrain
    {
        protected override Task<Either<Exception, EmptyOut>> RunInternal(EmptyIn input) =>
            Task.FromResult<Either<Exception, EmptyOut>>(new EmptyOut());
    }
}
