using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.Services.TrainLifecycleHook;
using Trax.Effect.Services.TrainLifecycleHookFactory;
using Trax.Effect.Tests.Data.InMemory.Integration.Fixtures;

namespace Trax.Effect.Tests.Data.InMemory.Integration.IntegrationTests;

/// <summary>
/// Without <c>SaveTrainParameters</c>, a completed run still serializes its output for the
/// lifecycle hooks, and hooks are what broadcast a run to other processes and subscribers. A
/// <see cref="TraxSensitiveAttribute"/> property is masked there as it is in the stored output.
/// </summary>
public class SensitiveOutputHookTests : TestSetup
{
    private static readonly OutputRecordingHook Hook = new();

    public override ServiceProvider ConfigureServices(IServiceCollection services) =>
        services
            .AddSingleton<ITrainLifecycleHookFactory>(new OutputRecordingHookFactory(Hook))
            .AddScopedTraxRoute<ISensitiveOutputTrain, SensitiveOutputTrain>()
            .BuildServiceProvider();

    [Test]
    public async Task The_output_a_hook_sees_has_its_sensitive_property_masked()
    {
        var train = Scope.ServiceProvider.GetRequiredService<ISensitiveOutputTrain>();

        await train.Run(Unit.Default);

        Hook.Output.Should().Contain("ada").And.NotContain("tok-secret");
    }

    public sealed class Issued
    {
        public string User { get; set; } = "";

        [TraxSensitive]
        public string Token { get; set; } = "";
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

    private class SensitiveOutputTrain : ServiceTrain<Unit, Issued>, ISensitiveOutputTrain
    {
        protected override Task<Either<Exception, Issued>> Junctions() =>
            Task.FromResult<Either<Exception, Issued>>(
                new Issued { User = "ada", Token = "tok-secret" }
            );
    }

    private interface ISensitiveOutputTrain : IServiceTrain<Unit, Issued> { }
}
