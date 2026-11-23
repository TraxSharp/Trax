using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Trax.StateMachine.Features;

namespace Trax.StateMachine.Gherkin;

/// <summary>
/// What features derive: one IR per machine, by id, the host file and the access report, or the problems that stopped
/// it. A derivation with problems has no files. <see cref="Warnings"/> are designs that derive and may still not hold
/// on the host; they stop nothing.
/// </summary>
public sealed record DerivedMachines(
    IReadOnlyDictionary<string, string> Irs,
    string? Host,
    string? Access,
    IReadOnlyList<FeatureProblem> Problems,
    IReadOnlyList<FeatureProblem> UiSteps
)
{
    /// <summary>Whether the features derived: no problem, and every file written.</summary>
    public bool Succeeded => Problems.Count == 0 && Host is not null;

    /// <summary>
    /// Designs that derive and may still not hold on the host, such as a policy that waits for another policy's result,
    /// at the file and line each is about. They write nothing different and do not change <see cref="Succeeded"/>.
    /// </summary>
    public IReadOnlyList<FeatureProblem> Warnings { get; init; } = [];
}
