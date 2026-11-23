using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Trax.StateMachine.Features;

/// <summary>
/// An actor, with the noun a feature names a person by (<c>Given Ada is a customer</c>), and its roles or any signed-in
/// caller.
/// </summary>
internal sealed record GlossaryActor(
    string Name,
    string Noun,
    IReadOnlyList<string> Roles,
    bool AnyAuthenticated
);
