namespace Trax.StateMachine.Gherkin.Lexical;

/// <summary>A context write, <c>target to square</c>: from a field of the line's source, a constant or a value.</summary>
internal sealed record LxSet(string Field, LxValue Value, int Line);
