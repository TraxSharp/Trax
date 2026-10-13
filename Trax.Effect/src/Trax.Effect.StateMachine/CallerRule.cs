namespace Trax.Effect.StateMachine;

/// <summary>Which kind of caller a <see cref="CallerRule"/> admits.</summary>
internal enum CallerRuleKind
{
    /// <summary>Any caller, signed in or not.</summary>
    Anonymous,

    /// <summary>Any authenticated caller.</summary>
    AnyAuthenticated,

    /// <summary>Only the instance's owner (reads only), and the requirements beside it.</summary>
    OwnerOnly,

    /// <summary>An authenticated caller who holds a role of every set and satisfies every policy.</summary>
    Requires,
}

/// <summary>
/// Who may fire an event, start an instance or read one, as the machine declares it with <c>Requires</c>,
/// <c>RequiresPolicy</c>, <c>AnyAuthenticated</c>, <c>Anonymous</c> or (for a read) <c>OwnerOnly</c>. Roles compare
/// ordinally, as <c>[TraxAuthorize]</c> roles do. Chained <c>Requires</c> calls are separate sets that each must be
/// met; the roles of one call are alternatives.
/// </summary>
/// <param name="Kind">Which kind of caller it admits.</param>
/// <param name="RoleSets">The role sets, each met by holding one of its roles. Empty when the rule names none.</param>
/// <param name="Policies">The authorization policies the caller must satisfy, all of them.</param>
internal sealed record CallerRule(
    CallerRuleKind Kind,
    IReadOnlyList<IReadOnlyList<string>> RoleSets,
    IReadOnlyList<string> Policies
)
{
    /// <summary>Any caller.</summary>
    public static readonly CallerRule Anyone = new(CallerRuleKind.Anonymous, [], []);

    /// <summary>Any authenticated caller.</summary>
    public static readonly CallerRule Authenticated = new(CallerRuleKind.AnyAuthenticated, [], []);

    /// <summary>
    /// "Gated": the rule is stricter than <c>AnyAuthenticated</c>, because it names a role or a policy. A gated event's
    /// writes are the server's alone, a gated event may not enter the start state, and a machine with a gated event
    /// or start is not served by the generic save.
    /// </summary>
    public bool Gated => RoleSets.Count > 0 || Policies.Count > 0;

    /// <summary>Whether a caller who meets <paramref name="other"/> always meets this rule, and back.</summary>
    public bool SameAs(CallerRule other) =>
        Kind == other.Kind
        && Normal(RoleSets).SequenceEqual(Normal(other.RoleSets))
        && Policies
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .SequenceEqual(
                other.Policies.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal),
                StringComparer.Ordinal
            );

    private static IEnumerable<string> Normal(IReadOnlyList<IReadOnlyList<string>> sets) =>
        sets.Select(set =>
                string.Join(
                    "\u0001",
                    set.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
                )
            )
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);

    /// <summary>The rule as a reader would write it, for a refusal or the census.</summary>
    public override string ToString()
    {
        var parts = new List<string>();
        if (Kind == CallerRuleKind.OwnerOnly)
            parts.Add("OwnerOnly");
        foreach (var set in RoleSets)
            parts.Add(
                set.Count == 1
                    ? $"Requires({set[0]})"
                    : $"Requires(one of {string.Join(", ", set)})"
            );
        foreach (var policy in Policies)
            parts.Add($"RequiresPolicy({policy})");
        return Kind switch
        {
            CallerRuleKind.Anonymous => "Anonymous",
            CallerRuleKind.AnyAuthenticated => "AnyAuthenticated",
            _ => string.Join(" and ", parts),
        };
    }
}

/// <summary>Collects the rule calls of one event, start or read, and checks they make one rule.</summary>
internal sealed class CallerRuleDraft(string where, bool read)
{
    private readonly List<IReadOnlyList<string>> _roleSets = [];
    private readonly List<string> _policies = [];
    private readonly List<CallerRuleKind> _kinds = [];

    /// <summary>Whether any rule call was made.</summary>
    public bool Declared => _kinds.Count > 0 || _roleSets.Count > 0 || _policies.Count > 0;

    public void Requires(string[] roles)
    {
        ArgumentNullException.ThrowIfNull(roles);
        if (roles.Length == 0 || roles.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException(
                $"{where} declares Requires with no role, or a blank one. Name at least one role.",
                nameof(roles)
            );
        _roleSets.Add(roles.Distinct(StringComparer.Ordinal).ToArray());
    }

    public void RequiresPolicy(string policy)
    {
        if (string.IsNullOrWhiteSpace(policy))
            throw new ArgumentException(
                $"{where} declares RequiresPolicy with a blank policy name.",
                nameof(policy)
            );
        _policies.Add(policy);
    }

    public void Kind(CallerRuleKind kind) => _kinds.Add(kind);

    /// <summary>The rule declared, or <c>null</c> when none was.</summary>
    /// <exception cref="InvalidOperationException">The calls do not make one rule.</exception>
    public CallerRule? Build()
    {
        if (!Declared)
            return null;
        var kinds = _kinds.Distinct().ToList();
        var requirements = _roleSets.Count > 0 || _policies.Count > 0;
        if (
            kinds.Count > 1
            || (
                kinds is [CallerRuleKind.Anonymous or CallerRuleKind.AnyAuthenticated]
                && requirements
            )
        )
            throw new InvalidOperationException(
                $"{where} declares conflicting rules ({string.Join(", ", kinds)}"
                    + (requirements ? ", and a requirement" : "")
                    + "). Declare one: Requires and RequiresPolicy may be combined, "
                    + (read ? "with or without OwnerOnly; " : "")
                    + "AnyAuthenticated and Anonymous stand alone."
            );
        var kind = kinds is [var only] ? only : CallerRuleKind.Requires;
        // A read rule that names requirements still scopes the read to the owner: owner-only is its floor.
        if (read && kind == CallerRuleKind.Requires)
            kind = CallerRuleKind.OwnerOnly;
        return new CallerRule(
            kind,
            _roleSets.ToList(),
            _policies.Distinct(StringComparer.Ordinal).ToList()
        );
    }
}

/// <summary>
/// Every rule a machine declares: each event's, by trigger name, and its <c>start</c>'s, reads' and
/// <c>discard</c>'s. A trigger, a read or the discard with no entry declared none.
/// </summary>
/// <param name="Events">Each declared event's rule, by trigger name.</param>
/// <param name="Start">The <c>start</c> rule, or <c>null</c>.</param>
/// <param name="Get">The <c>get</c> rule, or <c>null</c>.</param>
/// <param name="Watch">The <c>machineChanged</c> rule, or <c>null</c>.</param>
/// <param name="Discard">The <c>discard</c> rule, or <c>null</c>.</param>
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
        null,
        null,
        null
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
            yield return ("machineChanged", Watch);
        if (Discard is not null)
            yield return ("discard", Discard);
    }
}
