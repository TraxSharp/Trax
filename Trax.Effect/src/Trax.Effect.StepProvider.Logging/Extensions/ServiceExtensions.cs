using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Extensions;
using Trax.Effect.StepProvider.Logging.Services.StepLoggerFactory;
using Trax.Effect.StepProvider.Logging.Services.StepLoggerProvider;
using TraxEffectBuilder = Trax.Effect.Configuration.TraxEffectBuilder.TraxEffectBuilder;

namespace Trax.Effect.StepProvider.Logging.Extensions;

public static class ServiceExtensions
{
    /// <summary>
    /// Adds a step-level logger that records step names, durations, and optionally serialized input/output
    /// for each step in a train. Log entries are written at the configured effect log level.
    /// </summary>
    /// <typeparam name="TBuilder">The builder type (supports chaining through promoted builders).</typeparam>
    /// <param name="configurationBuilder">The effect builder.</param>
    /// <param name="serializeStepData">
    /// When <c>true</c>, step input and output are serialized to JSON and included in log entries.
    /// Defaults to <c>false</c> to avoid performance overhead.
    /// </param>
    /// <returns>The builder for chaining.</returns>
    public static TBuilder AddStepLogger<TBuilder>(
        this TBuilder configurationBuilder,
        bool serializeStepData = false
    )
        where TBuilder : TraxEffectBuilder
    {
        configurationBuilder.SerializeStepData = serializeStepData;
        configurationBuilder.ServiceCollection.AddTransient<
            IStepLoggerProvider,
            StepLoggerProvider
        >();

        configurationBuilder.AddStepEffect<StepLoggerFactory>();
        return configurationBuilder;
    }
}
