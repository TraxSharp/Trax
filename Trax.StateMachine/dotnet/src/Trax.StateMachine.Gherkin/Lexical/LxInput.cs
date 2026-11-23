namespace Trax.StateMachine.Gherkin.Lexical;

/// <summary>A train input from a context field, or from the owner (<see cref="From"/> is <c>$owner</c>).</summary>
internal sealed record LxInput(string Field, string From, int Line);
