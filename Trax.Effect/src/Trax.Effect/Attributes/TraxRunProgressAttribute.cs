namespace Trax.Effect.Attributes;

/// <summary>
/// Lets the caller who started a run of this train follow its progress, through the <c>runProgress</c> subscription
/// and query: each step's name and status as it runs, and how the run ended. Only that caller: a run nobody started
/// directly (a schedule, an operator, the dashboard) has no one who may follow it. See Trax.Docs/adr/0051-only-the-caller-who-started-a-run-may-follow-its-progress.md.
/// </summary>
/// <remarks>
/// A step's answer and confidence, and what ran on a decision track, are withheld unless <see cref="Answers"/> is
/// set; a failure shows its message only when a junction threw a <c>TrainException</c>. The train needs no
/// <c>[TraxBroadcast]</c>: a broadcast subscriber never sees a run that only this attribute publishes.
/// </remarks>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/graphql-api/run-progress">SDK reference</seealso>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class TraxRunProgressAttribute : Attribute
{
    /// <summary>Whether a step's answer, confidence and decision track are shown to the run's starter.</summary>
    public bool Answers { get; init; }
}
