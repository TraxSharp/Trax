using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Logging.Services.JunctionLoggerFactory;
using Trax.Effect.JunctionProvider.Logging.Services.JunctionLoggerProvider;
using TraxEffectBuilder = Trax.Effect.Configuration.TraxEffectBuilder.TraxEffectBuilder;

namespace Trax.Effect.JunctionProvider.Logging.Extensions;

/// <summary>
/// Adds <c>AddJunctionLogger</c> to the effect builder, which logs every junction of every service
/// train through <see cref="Microsoft.Extensions.Logging.ILogger"/>.
/// </summary>
public static class ServiceExtensions
{
    /// <summary>
    /// Adds a junction-level logger that logs each junction's metadata (name, timing, railway state) before
    /// and after it runs, and optionally its serialized output. Log entries are written at the configured
    /// effect log level under the category
    /// <c>Trax.Effect.JunctionProvider.Logging.Services.JunctionLoggerProvider.JunctionLoggerProvider</c>.
    /// </summary>
    /// <typeparam name="TBuilder">The builder type (supports chaining through promoted builders).</typeparam>
    /// <param name="configurationBuilder">The effect builder.</param>
    /// <param name="serializeJunctionData">
    /// When <c>true</c>, a successful junction's output is serialized to JSON (with <c>[TraxSensitive]</c>
    /// members masked) into its metadata's <c>OutputJson</c> and so appears in the after-junction entry.
    /// The copy follows the train's lifecycle-hook output policy: none for a train whose output
    /// <c>SaveTrainParameters</c> excludes, and at most <c>MaxParameterBytes</c> (1 MiB by default) before it
    /// is replaced by a <c>_truncated</c> placeholder. An output that cannot be serialized is logged as an
    /// <c>_unserializable</c> placeholder, and never fails the train. Junction input is not serialized.
    /// Defaults to <c>false</c> to avoid performance overhead.
    /// </param>
    /// <returns>The builder for chaining.</returns>
    public static TBuilder AddJunctionLogger<TBuilder>(
        this TBuilder configurationBuilder,
        bool serializeJunctionData = false
    )
        where TBuilder : TraxEffectBuilder
    {
        configurationBuilder.SerializeJunctionData = serializeJunctionData;

        configurationBuilder.AddJunctionEffect<JunctionLoggerFactory>();
        return configurationBuilder;
    }
}
