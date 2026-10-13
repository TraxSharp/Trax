namespace Trax.Effect.StateMachine.Persistence;

/// <summary>What one firing of a due timer did, from <see cref="InvokeOutcomeReconciler.FireDueTimersOnce"/>.</summary>
/// <param name="InstanceId">The instance whose timer was due.</param>
/// <param name="Outcome">
/// One of <see cref="Fired"/>, <see cref="Lost"/>, <see cref="NotHere"/>, <see cref="Cleared"/>, <see cref="Waiting"/>,
/// <see cref="Failed"/> or <see cref="Retrying"/>.
/// </param>
internal sealed record TimerFireResult(Guid InstanceId, string Outcome)
{
    /// <summary>The timer moved the instance: the conditional update matched, once.</summary>
    public const string Fired = "fired";

    /// <summary>Something else wrote the instance first (a client event, another host's sweep), so nothing moved.</summary>
    public const string Lost = "lost";

    /// <summary>
    /// This host cannot read the row (a newer definition wrote it), so the timer is left to a host that can and tried
    /// here again only after a back-off.
    /// </summary>
    public const string NotHere = "not-here";

    /// <summary>The row's state no longer has a timer (a deploy removed it), so its due time was cleared, not fired.</summary>
    public const string Cleared = "cleared";

    /// <summary>The instance's effect is running, so the timer waits for its claim to be resolved.</summary>
    public const string Waiting = "waiting";

    /// <summary>
    /// The timer could not be applied (its target refused the context, or its fire threw on every attempt), so its due
    /// time was cleared and counted.
    /// </summary>
    public const string Failed = "failed";

    /// <summary>
    /// The fire threw, so its transaction rolled back alone; it was counted and is tried again after a back-off.
    /// </summary>
    public const string Retrying = "retrying";

    /// <summary>Whether the sweep moved the row on: fired it, cleared it, or cleared a timer that could not fire.</summary>
    public bool Moved => Outcome is Fired or Cleared or Failed;
}
