using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Core.Decisions;

namespace Trax.Effect.Services.Decisions;

/// <summary>
/// The <see cref="IDecisionObserver"/> Trax.Core finds in the container when Trax registers one of
/// its own: it tells every registered observer, so decision recording, junction events and a
/// host's own observer each hear every decision, rather than the one registered first or last
/// silently replacing the others.
/// </summary>
/// <remarks>
/// <para>The required observers are told first, in the order they were registered, and the first
/// failure among them propagates, which fails the step as it would for that observer alone: a
/// decision that could not be recorded is not acted on, and is not reported as made either. Then the
/// best-effort observers are told, each failure logged and swallowed.</para>
///
/// <para>An observer registered before Trax's own is folded in when Trax's is added. One registered
/// after it, as <see cref="IDecisionObserver"/>, is what the container returns instead, so
/// <see cref="DecisionObserverCheck"/> refuses the host's start and every run on it: decision
/// recording would not record, and junction events would not withhold what a track gives away.
/// Register it before <c>AddTrax</c> to have both.</para>
///
/// <para>Each observer is built on its own. A host's observer known to be best effort that cannot
/// be built is left out with a warning; any other that cannot be built fails the composite, and so
/// every decision step and, through the check, the host's start.</para>
/// </remarks>
internal sealed class CompositeDecisionObserver : IDecisionObserver
{
    private readonly IReadOnlyList<IDecisionObserver> _required;
    private readonly IReadOnlyList<IDecisionObserver> _bestEffort;
    private readonly ILogger<CompositeDecisionObserver>? _logger;

    public CompositeDecisionObserver(
        IServiceProvider services,
        IEnumerable<DecisionObserverPart> parts,
        ILogger<CompositeDecisionObserver>? logger = null
    )
    {
        _logger = logger;
        var observers = new List<IDecisionObserver>();

        // Built one by one, so a best-effort observer that cannot be built costs only itself. Any
        // other propagates: whether it was required cannot be told from an observer never built.
        foreach (var part in parts)
        {
            try
            {
                observers.Add(services.GetRequiredKeyedService<IDecisionObserver>(part.Key));
            }
            catch (Exception e) when (part.Skippable)
            {
                _logger?.LogWarning(
                    e,
                    "A best-effort decision observer could not be built; it is left out, and the "
                        + "other observers are told about every decision."
                );
            }
        }

        _required = observers.Where(o => o.Required).ToList();
        _bestEffort = observers.Where(o => !o.Required).ToList();
    }

    /// <summary>
    /// True when any part is required. Only a required part's failure leaves the composite, so a
    /// failure that reaches Trax.Core always fails the step.
    /// </summary>
    public bool Required => _required.Count > 0;

    /// <inheritdoc />
    public Task Decided(DecisionMade decision, CancellationToken cancellationToken) =>
        TellAll((o, ct) => o.Decided(decision, ct), cancellationToken);

    /// <inheritdoc />
    public Task Routed(TrackRouted routing, CancellationToken cancellationToken) =>
        TellAll((o, ct) => o.Routed(routing, ct), cancellationToken);

    /// <inheritdoc />
    public Task Refused(DecisionRefused refusal, CancellationToken cancellationToken) =>
        TellAll((o, ct) => o.Refused(refusal, ct), cancellationToken);

    private async Task TellAll(
        Func<IDecisionObserver, CancellationToken, Task> tell,
        CancellationToken cancellationToken
    )
    {
        foreach (var observer in _required)
            await tell(observer, cancellationToken).ConfigureAwait(false);

        foreach (var observer in _bestEffort)
        {
            try
            {
                await tell(observer, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _logger?.LogWarning(
                    e,
                    "Decision observer ({Observer}) threw; it is best effort, so the decision stands.",
                    observer.GetType().Name
                );
            }
        }
    }
}
