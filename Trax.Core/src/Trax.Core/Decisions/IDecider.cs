namespace Trax.Core.Decisions;

/// <summary>
/// Answers typed questions about one state: which of a set of options, where on an ordered scale,
/// or how likely yes.
/// </summary>
/// <remarks>
/// This is the seam a train's decisions go through. A decision model that returns typed,
/// calibrated answers implements it in an adapter, and so can a rule table
/// (<see cref="RuleDecider"/>), a classifier, a larger model for escalation
/// (<see cref="CascadingDecider"/>), or a test double. Trax turns the train's typed declaration into
/// questions and turns the answers back into typed decisions, so an implementation never sees a
/// .NET type it must understand and never hands back one the train did not offer.
///
/// <para>A decider is found the way a junction input is: in Memory, then in the container. One
/// registration serves every decision in every train; <c>DecidedBy&lt;TDecider&gt;</c> names a
/// different one for a single decision.</para>
/// </remarks>
public interface IDecider
{
    /// <summary>
    /// Answers every question in <paramref name="request"/>.
    /// </summary>
    /// <returns>
    /// One answer per question, keyed by <see cref="Question.Key"/>. Throwing fails the run: an
    /// error is not a decision, so no track is taken on its behalf.
    /// </returns>
    Task<DecisionResult> Decide(DecisionRequest request, CancellationToken cancellationToken);
}
