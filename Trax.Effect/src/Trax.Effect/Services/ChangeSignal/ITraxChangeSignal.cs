namespace Trax.Effect.Services.ChangeSignal;

/// <summary>
/// Fire-and-forget notification that a domain's data changed. Write paths call
/// <see cref="Notify"/> right after a successful commit. A background coalescer
/// (<see cref="ChangeSignalCoalescer"/>) collapses bursts within a short window and
/// forwards one signal per distinct domain to every registered
/// <see cref="IChangeSignalSink"/> (the in-process GraphQL topic, the cross-process
/// broadcaster). <see cref="Notify"/> never throws and never blocks the caller, so it
/// is safe to call from a hot write path; under sustained pressure signals are dropped
/// rather than queued unbounded.
/// </summary>
public interface ITraxChangeSignal
{
    /// <summary>Signals that <paramref name="domain"/>'s data changed.</summary>
    void Notify(ChangeDomain domain);
}
