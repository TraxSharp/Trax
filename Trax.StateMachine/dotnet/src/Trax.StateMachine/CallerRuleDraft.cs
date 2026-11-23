namespace Trax.StateMachine;

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

    /// <summary>The rule declared, or <see langword="null"/> when none was.</summary>
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
