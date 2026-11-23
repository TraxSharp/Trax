using System.Text.RegularExpressions;

namespace Trax.StateMachine.Gherkin.Reqnroll;

/// <summary>An actor of the glossary, as a credential is minted for one: its name, its roles, or any signed-in caller.</summary>
public sealed record FeatureActor(string Name, IReadOnlyList<string> Roles, bool AnyAuthenticated);
