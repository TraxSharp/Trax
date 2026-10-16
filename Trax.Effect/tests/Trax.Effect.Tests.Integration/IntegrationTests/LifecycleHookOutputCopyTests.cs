using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Functional;
using Trax.Effect.Configuration.TraxEffectBuilder;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Effect.Provider.Parameter.Services.ParameterEffectProviderFactory;
using Trax.Effect.Services.EffectRegistry;
using Trax.Effect.Services.LifecycleHookOutputPolicy;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.Services.TrainLifecycleHook;
using Trax.Effect.Services.TrainLifecycleHookFactory;

namespace Trax.Effect.Tests.Integration.IntegrationTests;

/// <summary>
/// The copy of a train's output that lifecycle hooks receive when no stored copy was written
/// follows the stored copy's decision and ceiling.
/// </summary>
public class LifecycleHookOutputCopyTests
{
    [Test]
    public async Task Without_SaveTrainParameters_a_small_output_reaches_the_hooks_in_full()
    {
        var output = await RunAndCapture(rows: 3, effects => effects);

        output.Should().NotBeNull();
        output
            .Should()
            .NotContain("$id", "a hook forwards the output; it carries no serializer metadata");
        JsonDocument.Parse(output!).RootElement.GetArrayLength().Should().Be(3);
    }

    [Test]
    public async Task Without_SaveTrainParameters_the_copy_is_bounded_by_the_default_ceiling()
    {
        // About 1.4 MiB serialized.
        var output = await RunAndCapture(rows: 100_000, effects => effects);

        output
            .Should()
            .Be(
                $$"""{"_truncated": true, "_maxBytes": {{DefaultLifecycleHookOutputPolicy.DefaultMaxCopyBytes}}}"""
            );
    }

    [Test]
    public async Task With_SaveOutputs_off_the_hooks_get_no_copy()
    {
        var output = await RunAndCapture(
            rows: 3,
            effects => effects.SaveTrainParameters(configure: cfg => cfg.SaveOutputs = false)
        );

        output.Should().BeNull("the host chose not to serialize outputs");
    }

    [Test]
    public async Task With_the_parameter_effect_switched_off_the_copy_is_bounded_by_MaxParameterBytes()
    {
        var output = await RunAndCapture(
            rows: 1_000,
            effects => effects.SaveTrainParameters(configure: cfg => cfg.MaxParameterBytes = 512),
            provider =>
                provider
                    .GetRequiredService<IEffectRegistry>()
                    .Disable<ParameterEffectProviderFactory>()
        );

        output.Should().Be("""{"_truncated": true, "_maxBytes": 512}""");
    }

    [Test]
    public async Task A_train_that_withholds_its_output_gives_the_hooks_none_without_SaveTrainParameters()
    {
        var output = await RunAndCapture<IWithheldRowsTrain, WithheldRowsTrain>(
            rows: 3,
            effects => effects
        );

        output.Should().BeNull("a train that withholds its output publishes none to any hook");
    }

    [Test]
    public async Task A_train_that_withholds_its_output_gives_the_hooks_none_and_its_row_stores_none()
    {
        var (output, outputObject, stored) = await RunAndCaptureStored<
            IWithheldRowsTrain,
            WithheldRowsTrain
        >(rows: 3, effects => effects.SaveTrainParameters());

        output.Should().BeNull("the hooks never see the output, stored or not");
        outputObject.Should().BeNull("nor the object it was serialized from");
        stored
            .Should()
            .BeNull("the parameter effect stores no output for a train that withholds it");
    }

    [Test]
    public async Task A_plain_train_still_gives_the_hooks_its_stored_output()
    {
        var (output, _, stored) = await RunAndCaptureStored<IRowsTrain, RowsTrain>(
            rows: 3,
            effects => effects.SaveTrainParameters()
        );

        output.Should().NotBeNull().And.Be(stored);
    }

    private static Task<string?> RunAndCapture(
        int rows,
        Func<TraxEffectBuilder, TraxEffectBuilder> configure,
        Action<IServiceProvider>? arrange = null
    ) => RunAndCapture<IRowsTrain, RowsTrain>(rows, configure, arrange);

    private static async Task<string?> RunAndCapture<TTrain, TImpl>(
        int rows,
        Func<TraxEffectBuilder, TraxEffectBuilder> configure,
        Action<IServiceProvider>? arrange = null
    )
        where TTrain : class, IServiceTrain<int, List<string>>
        where TImpl : class, TTrain =>
        (await RunAndCaptureStored<TTrain, TImpl>(rows, configure, arrange)).Output;

    private static async Task<(
        string? Output,
        object? OutputObject,
        string? Stored
    )> RunAndCaptureStored<TTrain, TImpl>(
        int rows,
        Func<TraxEffectBuilder, TraxEffectBuilder> configure,
        Action<IServiceProvider>? arrange = null
    )
        where TTrain : class, IServiceTrain<int, List<string>>
        where TImpl : class, TTrain
    {
        var hook = new OutputRecordingHook();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax => trax.AddEffects(effects => configure(effects.UseInMemory())));
        services
            .AddSingleton<ITrainLifecycleHookFactory>(new OutputRecordingHookFactory(hook))
            .AddScopedTraxRoute<TTrain, TImpl>();
        await using var provider = services.BuildServiceProvider();
        arrange?.Invoke(provider);

        using var scope = provider.CreateScope();
        var train = scope.ServiceProvider.GetRequiredService<TTrain>();
        await train.Run(rows);

        hook.Completed.Should().BeTrue();
        return (hook.Output, hook.OutputObject, train.Metadata?.Output);
    }

    private sealed class OutputRecordingHook : ITrainLifecycleHook
    {
        public bool Completed { get; private set; }
        public string? Output { get; private set; }

        public object? OutputObject { get; private set; }

        public Task OnCompleted(Metadata metadata, CancellationToken ct)
        {
            Completed = true;
            Output = metadata.Output;
            OutputObject = metadata.GetOutputObject();
            return Task.CompletedTask;
        }
    }

    private sealed class OutputRecordingHookFactory(OutputRecordingHook hook)
        : ITrainLifecycleHookFactory
    {
        public ITrainLifecycleHook Create() => hook;
    }

    private class RowsTrain : ServiceTrain<int, List<string>>, IRowsTrain
    {
        protected override Task<Either<Exception, List<string>>> Junctions() =>
            Task.FromResult<Either<Exception, List<string>>>(
                Enumerable.Range(0, TrainInput).Select(i => $"row-{i}").ToList()
            );
    }

    public interface IRowsTrain : IServiceTrain<int, List<string>> { }

    [OutputWithheldFromLifecycleHooks]
    private class WithheldRowsTrain : ServiceTrain<int, List<string>>, IWithheldRowsTrain
    {
        protected override Task<Either<Exception, List<string>>> Junctions() =>
            Task.FromResult<Either<Exception, List<string>>>(
                Enumerable.Range(0, TrainInput).Select(i => $"row-{i}").ToList()
            );
    }

    public interface IWithheldRowsTrain : IServiceTrain<int, List<string>> { }
}
