using Microsoft.Extensions.Logging;
using Trax.Effect.Configuration.TraxEffectBuilder;

namespace Trax.Effect.Extensions;

public static class TraxEffectBuilderExtensions
{
    /// <summary>
    /// Sets the minimum log level for effect-related logging (e.g., data context operations, step effects).
    /// Defaults to <see cref="LogLevel.Debug"/>.
    /// </summary>
    /// <typeparam name="TBuilder">The builder type (supports chaining through promoted builders).</typeparam>
    /// <param name="builder">The effect builder.</param>
    /// <param name="logLevel">The minimum log level for effect operations.</param>
    /// <returns>The builder for chaining.</returns>
    public static TBuilder SetEffectLogLevel<TBuilder>(this TBuilder builder, LogLevel logLevel)
        where TBuilder : TraxEffectBuilder
    {
        builder.LogLevel = logLevel;
        return builder;
    }
}
