using FluentAssertions;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.Services.TrainLifecycleHook;
using Trax.Effect.Services.TrainLifecycleHookFactory;

namespace Trax.Effect.Tests.Integration.IntegrationTests;

/// <summary>
/// <c>ExcludeOutput</c> is documented as the way to skip serializing a known-large output
/// entirely, and <c>MaxParameterBytes</c> as the ceiling on every serialized parameter. The copy of
/// the output built for lifecycle hooks is a serialized parameter too.
/// </summary>
public class ExcludedOutputHookCopyTests
{
    private const int Ceiling = 1024;
    private static readonly OutputRecordingHook Hook = new();
    private ServiceProvider _provider = null!;

    [OneTimeSetUp]
    public void BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects =>
                effects
                    .UseInMemory()
                    .SaveTrainParameters(configure: cfg =>
                    {
                        cfg.MaxParameterBytes = Ceiling;
                        cfg.ExcludeOutput<IBigFetchTrain>();
                    })
            )
        );
        services
            .AddSingleton<ITrainLifecycleHookFactory>(new OutputRecordingHookFactory(Hook))
            .AddScopedTraxRoute<IBigFetchTrain, BigFetchTrain>();
        _provider = services.BuildServiceProvider();
    }

    [OneTimeTearDown]
    public async Task DisposeProvider() => await _provider.DisposeAsync();

    [Test]
    public async Task An_excluded_output_is_not_serialized_in_full_for_the_hooks()
    {
        using var scope = _provider.CreateScope();
        var train = scope.ServiceProvider.GetRequiredService<IBigFetchTrain>();

        await train.Run(Unit.Default);

        (Hook.Output?.Length ?? 0)
            .Should()
            .BeLessThanOrEqualTo(
                Ceiling,
                "the train's output is excluded from serialization and every serialized parameter "
                    + "is capped at MaxParameterBytes, yet a full copy was built and handed to "
                    + "every lifecycle hook"
            );
    }

    private sealed class OutputRecordingHook : ITrainLifecycleHook
    {
        public string? Output { get; private set; }

        public Task OnCompleted(Metadata metadata, CancellationToken ct)
        {
            Output = metadata.Output;
            return Task.CompletedTask;
        }
    }

    private sealed class OutputRecordingHookFactory(OutputRecordingHook hook)
        : ITrainLifecycleHookFactory
    {
        public ITrainLifecycleHook Create() => hook;
    }

    private class BigFetchTrain : ServiceTrain<Unit, List<string>>, IBigFetchTrain
    {
        protected override Task<Either<Exception, List<string>>> Junctions() =>
            Task.FromResult<Either<Exception, List<string>>>(
                Enumerable.Range(0, 10_000).Select(i => $"row-{i}").ToList()
            );
    }

    public interface IBigFetchTrain : IServiceTrain<Unit, List<string>> { }
}
