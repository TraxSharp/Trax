using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Trax.StateMachine.Features;

namespace Trax.StateMachine.Gherkin;

/// <summary>Something a feature says that derivation, or a run, does not accept, at the file and line it is about.</summary>
public sealed record FeatureProblem(string File, long Line, string Message)
{
    /// <inheritdoc />
    public override string ToString() => $"{File}:{Line}: {Message}";

    internal static FeatureProblem Of(Problem problem) =>
        new(problem.File, problem.Line, problem.Message);
}
