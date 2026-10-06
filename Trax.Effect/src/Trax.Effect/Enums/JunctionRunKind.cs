namespace Trax.Effect.Enums;

/// <summary>
/// What one step of a run's timeline is: a junction that ran, a question a routing step asked, or
/// the track a routing step took. Carried by junction events and stored in
/// <c>trax.junction_run.kind</c>.
/// </summary>
/// <remarks>
/// A routing step (<c>Decide</c>, <c>Switch</c>, <c>Gate</c>, <c>Scale</c>) is not a junction: it runs
/// inside the train's chain and reports only through Trax.Core's decision observer, which says what
/// kind of question was asked and which track was taken, but not which of the four steps asked it.
/// So a step is told by its question: a <c>Decide</c> or <c>Switch</c> asks a <see cref="Choice"/>,
/// a <c>Scale</c> a <see cref="Score"/>, a <c>Gate</c> a <see cref="YesNo"/>, and every routing step
/// that sends the run down a track adds a <see cref="Route"/>.
///
/// The values are pinned because SQLite stores the integer.
/// </remarks>
public enum JunctionRunKind
{
    /// <summary>An <c>EffectJunction</c> that ran.</summary>
    Junction = 0,

    /// <summary>A question answered by choosing one of its options (<c>Decide</c>, <c>Switch</c>).</summary>
    Choice = 1,

    /// <summary>A question answered by a position on ordered levels (<c>Scale</c>).</summary>
    Score = 2,

    /// <summary>A question answered by the probability of yes (<c>Gate</c>).</summary>
    YesNo = 3,

    /// <summary>The track a routing step sent the run down.</summary>
    Route = 4,
}
