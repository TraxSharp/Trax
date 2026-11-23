using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Trax.StateMachine.Features;

namespace Trax.StateMachine.Gherkin;

/// <summary>
/// What derivation wrote: each machine's IR (canonical JSON, by id) and the host file, or the problems that stopped it.
/// A derivation with problems writes nothing. <see cref="UiSteps"/> are the Thens no phrase resolves, which are about
/// the UI and are left to the browser runner. <see cref="Warnings"/> are designs that derive and may still not hold on
/// the host; they write nothing different and stop nothing.
/// </summary>
internal sealed record Derived(
    IReadOnlyDictionary<string, string> Irs,
    string? Host,
    string? Access,
    IReadOnlyList<Problem> Problems,
    IReadOnlyList<Problem> UiSteps,
    IReadOnlyList<Problem> Warnings
);
