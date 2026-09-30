using FluentAssertions;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Configuration;
using Trax.Mediator.Services.TrainAuthorization;
using Trax.Mediator.Services.TrainBus;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.IntegrationTests;

public partial class RunAndQueueExecutionTests
{
    [Test]
    public async Task TrainBus_TrainWithAnAsyncOnlyDisposableDependency_ReturnsItsOutput()
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var bus = scope.ServiceProvider.GetRequiredService<ITrainBus>();

        (await bus.RunAsync<string>(new AsyncScopedInput())).Should().Be("ran");
        (await bus.RunAsync<string>(new AsyncScopedInput(), CancellationToken.None))
            .Should()
            .Be("ran");

        var untyped = async () => await bus.RunAsync(new AsyncScopedInput());
        await untyped.Should().NotThrowAsync();

        var untypedWithToken = async () =>
            await bus.RunAsync(new AsyncScopedInput(), CancellationToken.None);
        await untypedWithToken.Should().NotThrowAsync();
    }

    [Test]
    public async Task TrainBus_FailingTrainWithAnAsyncOnlyDisposableDependency_SurfacesItsOwnFailure()
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var bus = scope.ServiceProvider.GetRequiredService<ITrainBus>();

        var act = async () =>
            await bus.RunAsync<string>(new FailingAsyncScopedInput(), CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*the train's own failure*");
    }

    [Test]
    public async Task AuthorizationValidator_AuthorizationServiceThatIsOnlyAsyncDisposable_Starts()
    {
        var services = new ServiceCollection();
        services.AddScoped<ITrainAuthorizationService, AsyncOnlyAuthorization>();
        await using var provider = services.BuildServiceProvider();

        var validator = new AuthorizationRegistrationValidator(
            new TrainDiscoveryService(services),
            new MediatorConfiguration(),
            provider
        );

        var act = async () => await validator.StartingAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    public sealed class AsyncOnlyResource : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    public record AsyncScopedInput;

    public interface IAsyncScopedTrain : IServiceTrain<AsyncScopedInput, string>;

    public class AsyncScopedTrain(AsyncOnlyResource resource)
        : ServiceTrain<AsyncScopedInput, string>,
            IAsyncScopedTrain
    {
        public AsyncOnlyResource Resource { get; } = resource;

        protected override Task<Either<Exception, string>> Junctions() =>
            Task.FromResult<Either<Exception, string>>("ran");
    }

    public record FailingAsyncScopedInput;

    public interface IFailingAsyncScopedTrain : IServiceTrain<FailingAsyncScopedInput, string>;

    public class FailingAsyncScopedTrain(AsyncOnlyResource resource)
        : ServiceTrain<FailingAsyncScopedInput, string>,
            IFailingAsyncScopedTrain
    {
        public AsyncOnlyResource Resource { get; } = resource;

        protected override Task<Either<Exception, string>> Junctions() =>
            Task.FromResult<Either<Exception, string>>(
                new ArgumentException("the train's own failure")
            );
    }

    public sealed class AsyncOnlyAuthorization : ITrainAuthorizationService, IAsyncDisposable
    {
        public Task AuthorizeAsync(
            TrainRegistration registration,
            CancellationToken ct = default
        ) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
