using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Trax.Api.GraphQL.Startup;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.Tests;

/// <summary>
/// The startup guard that fails fast when the operations surface is exposed without the services
/// its resolvers depend on, instead of masking a per-request "Unexpected Execution Error".
/// </summary>
[TestFixture]
public class TraxOperationsServiceValidatorTests
{
    private static IServiceProviderIsService IsService(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        configure(services);
        return services.BuildServiceProvider().GetRequiredService<IServiceProviderIsService>();
    }

    [Test]
    public async Task Throws_WhenIOperationsServiceMissing()
    {
        var validator = new TraxOperationsServiceValidator(
            IsService(_ => { }),
            mutationsExposed: false
        );

        await validator
            .Invoking(v => v.StartAsync(CancellationToken.None))
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*IOperationsService*");
    }

    [Test]
    public async Task Throws_WhenMutationsExposedButITraxSchedulerMissing()
    {
        var validator = new TraxOperationsServiceValidator(
            IsService(s => s.AddScoped(_ => Substitute.For<IOperationsService>())),
            mutationsExposed: true
        );

        await validator
            .Invoking(v => v.StartAsync(CancellationToken.None))
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*ITraxScheduler*");
    }

    [Test]
    public async Task DoesNotThrow_WhenBothRegistered()
    {
        var validator = new TraxOperationsServiceValidator(
            IsService(s =>
            {
                s.AddScoped(_ => Substitute.For<IOperationsService>());
                s.AddScoped(_ => Substitute.For<ITraxScheduler>());
            }),
            mutationsExposed: true
        );

        await validator
            .Invoking(v => v.StartAsync(CancellationToken.None))
            .Should()
            .NotThrowAsync();
    }

    [Test]
    public async Task QueriesOnly_WithIOperationsService_DoesNotRequireScheduler()
    {
        var validator = new TraxOperationsServiceValidator(
            IsService(s => s.AddScoped(_ => Substitute.For<IOperationsService>())),
            mutationsExposed: false
        );

        await validator
            .Invoking(v => v.StartAsync(CancellationToken.None))
            .Should()
            .NotThrowAsync();
    }
}
