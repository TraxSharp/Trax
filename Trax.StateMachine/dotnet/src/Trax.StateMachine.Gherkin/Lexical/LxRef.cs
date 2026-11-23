namespace Trax.StateMachine.Gherkin.Lexical;

/// <summary>A bare name: a constant, or a field of whatever the line reads from.</summary>
internal sealed record LxRef(string Name, int Line) : LxValue(Line);
