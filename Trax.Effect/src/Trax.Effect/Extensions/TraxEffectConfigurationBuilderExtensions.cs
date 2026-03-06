using Microsoft.Extensions.Logging;
using Trax.Effect.Configuration.TraxEffectBuilder;

namespace Trax.Effect.Extensions;

public static class TraxEffectBuilderExtensions
{
    public static TraxEffectBuilder SetEffectLogLevel(
        this TraxEffectBuilder builder,
        LogLevel logLevel
    )
    {
        builder.LogLevel = logLevel;
        return builder;
    }
}
