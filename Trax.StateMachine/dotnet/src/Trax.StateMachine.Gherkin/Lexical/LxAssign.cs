namespace Trax.StateMachine.Gherkin.Lexical;

/// <summary>A field and a value: <c>loaves is 0</c>, in <c>Starts with:</c>, <c>Example:</c> and a reduction's example.</summary>
internal sealed record LxAssign(string Field, LxValue Value, int Line);
