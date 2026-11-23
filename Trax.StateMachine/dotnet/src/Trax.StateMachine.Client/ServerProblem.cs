using System.Text.Json.Nodes;

namespace Trax.StateMachine.Client;

/// <summary>A refusal the server answered with, as a value. <see cref="Code"/> is the contract (<c>GUARD_FAILED</c>).</summary>
/// <param name="Code">The refusal's code as it travels.</param>
/// <param name="Message">What the server said, for a person. It is not part of the contract.</param>
public sealed record ServerProblem(string Code, string Message);
