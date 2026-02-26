using Trax.Effect.Configuration.Trax.CoreEffectBuilder;
using Microsoft.Extensions.Logging;

namespace Trax.Effect.Extensions;

public static class Trax.CoreEffectConfigurationBuilderExtensions
{
    public static Trax.CoreEffectConfigurationBuilder SetEffectLogLevel(
        this Trax.CoreEffectConfigurationBuilder builder,
        LogLevel logLevel
    )
    {
        builder.LogLevel = logLevel;
        return builder;
    }
}
