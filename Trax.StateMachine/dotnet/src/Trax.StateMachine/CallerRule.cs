namespace Trax.StateMachine;

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
        && Normal(RoleSets).SequenceEqual(Normal(other.RoleSets), StringComparer.Ordinal)
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
                    '\u0001',
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
