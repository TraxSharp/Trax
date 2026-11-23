namespace Trax.StateMachine;

/// <summary>
/// Every rule a machine declares: each event's, by trigger name, and its <c>start</c>'s, reads' and
/// <c>discard</c>'s. A trigger, a read or the discard with no entry declared none.
/// </summary>
/// <param name="Events">Each declared event's rule, by trigger name.</param>
/// <param name="Start">The <c>start</c> rule, or <see langword="null"/>.</param>
/// <param name="Get">The <c>get</c> rule, or <see langword="null"/>.</param>
/// <param name="Watch">The the push (<c>&lt;machine&gt;Changed</c>) rule, or <see langword="null"/>.</param>
/// <param name="Discard">The <c>discard</c> rule, or <see langword="null"/>.</param>
internal sealed record MachineRules(
    IReadOnlyDictionary<string, CallerRule> Events,
    CallerRule? Start,
    CallerRule? Get,
    CallerRule? Watch,
    CallerRule? Discard = null
)
{
    /// <summary>A machine that declares no rule at all.</summary>
    public static readonly MachineRules None = new(
        new Dictionary<string, CallerRule>(StringComparer.Ordinal),
        Start: null,
        Get: null,
        Watch: null
    );

    /// <summary>Whether the machine declares any rule, on an event, its start, a read or its discard.</summary>
    public bool Any =>
        Events.Count > 0
        || Start is not null
        || Get is not null
        || Watch is not null
        || Discard is not null;

    /// <summary>Whether any event, or the start, is gated: names a role or a policy.</summary>
    public bool AnyGated => Events.Values.Any(r => r.Gated) || Start is { Gated: true };

    /// <summary>Every rule the machine declares, with where it is.</summary>
    public IEnumerable<(string Where, CallerRule Rule)> All()
    {
        foreach (var (trigger, rule) in Events)
            yield return (trigger, rule);
        if (Start is not null)
            yield return ("start", Start);
        if (Get is not null)
            yield return ("get", Get);
        if (Watch is not null)
            yield return ("changed", Watch);
        if (Discard is not null)
            yield return ("discard", Discard);
    }
}
