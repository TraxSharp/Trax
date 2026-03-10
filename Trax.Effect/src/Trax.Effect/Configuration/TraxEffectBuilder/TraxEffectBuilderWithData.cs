namespace Trax.Effect.Configuration.TraxEffectBuilder;

/// <summary>
/// Builder state after a data provider (<c>UsePostgres()</c> or <c>UseInMemory()</c>) has been configured.
/// Extension methods that require a data provider (e.g., <c>AddDataContextLogging()</c>) target this type,
/// enforcing at compile time that a data provider is registered before they can be called.
/// </summary>
/// <remarks>
/// This type inherits from <see cref="TraxEffectBuilder"/>, so all general effect extension methods
/// (e.g., <c>AddJson()</c>, <c>SaveTrainParameters()</c>) remain available via method chaining.
/// </remarks>
public class TraxEffectBuilderWithData : TraxEffectBuilder
{
    /// <summary>
    /// Promotes a <see cref="TraxEffectBuilder"/> to a <see cref="TraxEffectBuilderWithData"/>
    /// after a data provider has been registered.
    /// </summary>
    /// <param name="source">The effect builder to promote.</param>
    public TraxEffectBuilderWithData(TraxEffectBuilder source)
        : base(source) { }
}
