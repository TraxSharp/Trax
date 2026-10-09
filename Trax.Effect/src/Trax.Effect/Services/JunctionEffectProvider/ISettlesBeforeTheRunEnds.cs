namespace Trax.Effect.Services.JunctionEffectProvider;

/// <summary>
/// A junction effect whose writes can still be landing after the junction that asked for them has
/// returned. The run waits for <see cref="Settle"/> before its terminal write, so nothing the effect
/// writes lands after the run's outcome.
/// </summary>
internal interface ISettlesBeforeTheRunEnds
{
    /// <summary>
    /// Completes once every write the effect has started has landed or failed. Never throws.
    /// </summary>
    Task Settle();
}
