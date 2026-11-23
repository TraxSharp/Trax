namespace Trax.StateMachine.Gherkin.Lexical;

internal sealed record LxList(IReadOnlyList<LxValue> Items, int Line) : LxValue(Line);
