// What any compiled example under effect/ takes as given, beyond the shared host: the effect
// builder a snippet written inside `AddEffects(effects => ...)` is handed.
global using static EffectDocsHost;

public static class EffectDocsHost
{
    public static Trax.Effect.Configuration.TraxEffectBuilder.TraxEffectBuilder effects =>
        throw new NotSupportedException("Compiled, never run.");
}
