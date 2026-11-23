using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Core.Decisions;

namespace Trax.Effect.Services.Decisions;

/// <summary>
/// One of the decision observers the composite tells, registered under its own key.
/// </summary>
/// <param name="Key">The key the observer is registered under.</param>
/// <param name="Tag">
/// Identifies a Trax registration, so registering it again adds nothing. Null for an observer the
/// host registered.
/// </param>
/// <param name="Skippable">
/// True when the observer is known, without building it, to be best effort, so one that cannot be
/// built is left out with a warning instead of failing every decision. Trax's own observers are
/// never skippable: decision recording is required, and junction events withhold what a track
/// would give away only when told of every routing. A host's observer is skippable only when it is
/// registered by type and that type does not implement <see cref="IDecisionObserver.Required"/>
/// itself, or as an instance that is not required.
/// </param>
internal sealed record DecisionObserverPart(object Key, Type? Tag, bool Skippable = false);
