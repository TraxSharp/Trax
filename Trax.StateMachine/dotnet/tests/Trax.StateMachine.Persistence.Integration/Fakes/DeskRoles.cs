using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Trax.StateMachine.Rules;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

// Machines that declare who may fire each event, start an instance and read one. Every one of them builds, so
// the assembly scan every InstanceHost runs can register them; a machine whose declaration a host must refuse is
// refused at startup, when it is served, never when it is built. None is marked [TraxMachine], for the same reason;
// the ones a test serves are left open, and the test registers their marked subclass (Fixtures/Marked) on its host.

/// <summary>The roles and the policy the ruled machines name.</summary>
public static class DeskRoles
{
    public const string Operator = "Operator";
    public const string Admin = "Admin";

    /// <summary>The policy <see cref="RuledDeskMachine"/>'s <c>PolicyOnly</c> event requires.</summary>
    public const string Policy = "desk-policy";

    /// <summary>
    /// A client acting for <paramref name="userKey"/> with exactly <paramref name="roles"/>, which satisfies
    /// <see cref="Policy"/> when it holds <see cref="Admin"/>.
    /// </summary>
    public static Actor.Client Client(string userKey, params string[] roles) =>
        new(
            userKey,
            role => roles.Contains(role, StringComparer.Ordinal),
            (policy, _) =>
                Task.FromResult(
                    string.Equals(policy, Policy, StringComparison.Ordinal)
                        && roles.Contains(Admin, StringComparer.Ordinal)
                )
        );
}
