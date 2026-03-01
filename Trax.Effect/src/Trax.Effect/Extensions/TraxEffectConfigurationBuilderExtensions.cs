using Microsoft.Extensions.Logging;
using Trax.Effect.Configuration.TraxEffectBuilder;

namespace Trax.Effect.Extensions;

public static class TraxEffectConfigurationBuilderExtensions
{
    public static TraxEffectConfigurationBuilder SetEffectLogLevel(
        this TraxEffectConfigurationBuilder builder,
        LogLevel logLevel
    )
    {
        builder.LogLevel = logLevel;
        return builder;
    }
}
