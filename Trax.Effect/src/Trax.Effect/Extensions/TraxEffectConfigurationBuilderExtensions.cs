using Microsoft.Extensions.Logging;
using Trax.Effect.Configuration.TraxEffectBuilder;

namespace Trax.Effect.Extensions;

/// <summary>
/// General settings on the effect builder that apply regardless of which providers are added, such as
/// <c>SetEffectLogLevel()</c>.
/// </summary>
public static class TraxEffectBuilderExtensions
{
    /// <summary>
    /// Sets the level the junction logger (<c>AddJunctionLogger</c>) and the JSON effect (<c>AddJson</c>)
    /// write their entries at. Defaults to <see cref="LogLevel.Debug"/>.
    /// </summary>
    /// <remarks>
    /// It is not a filter: it filters nothing out, and whether an entry is kept is decided by the
    /// logging configuration's minimum level for its category, as for any other log line. Raise it
    /// (to <see cref="LogLevel.Information"/>, say) to keep those entries under a configuration
    /// whose minimum would drop <see cref="LogLevel.Debug"/>.
    /// </remarks>
    /// <typeparam name="TBuilder">The builder type (supports chaining through promoted builders).</typeparam>
    /// <param name="builder">The effect builder.</param>
    /// <param name="logLevel">The level those entries are written at.</param>
    /// <returns>The builder for chaining.</returns>
    public static TBuilder SetEffectLogLevel<TBuilder>(this TBuilder builder, LogLevel logLevel)
        where TBuilder : TraxEffectBuilder
    {
        builder.LogLevel = logLevel;
        return builder;
    }
}
