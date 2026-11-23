using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

namespace Trax.StateMachine.Client;

/// <summary>The last refusal or transport failure, shown until the next event the server confirms.</summary>
/// <param name="Kind">Whether it was refused or never got through.</param>
/// <param name="Code">The contract: a refusal code (<c>GUARD_FAILED</c>) or one of <see cref="ClientProblemCodes"/>.</param>
/// <param name="Message">What to tell a person. It is not part of the contract.</param>
public sealed record MachineProblem(MachineProblemKind Kind, string Code, string Message);
