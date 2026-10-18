using Trax.Samples.ApiDashboard.Browser.Factories;

namespace Trax.Samples.ApiDashboard.Browser.Fixtures;

/// <summary>
/// Who signs in to the console: an API key the host registers, its principal id and roles. Each key carries the
/// <c>do-not-use-in-production</c> marker (central ADR 0035).
/// </summary>
public sealed record Caller(string Key, string Id, IReadOnlyList<string> Roles)
{
    /// <summary>Past the operations gate and allowed to manage persisted operations.</summary>
    public static readonly Caller Operator = new(
        "console-operator-key-do-not-use-in-production",
        "operator",
        [ConsoleHost.OperatorRole, ConsoleHost.ApiAdminRole]
    );

    /// <summary>Signed in, and refused by the operations gate.</summary>
    public static readonly Caller Refused = new(
        "console-refused-key-do-not-use-in-production",
        "visitor",
        []
    );

    /// <summary>Past the operations gate, without persisted-operation management.</summary>
    public static readonly Caller NoManagement = new(
        "console-no-management-key-do-not-use-in-production",
        "on-call",
        [ConsoleHost.OperatorRole]
    );

    public static IReadOnlyList<Caller> All { get; } = [Operator, Refused, NoManagement];
}
