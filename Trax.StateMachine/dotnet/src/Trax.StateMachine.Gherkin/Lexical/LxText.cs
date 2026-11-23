namespace Trax.StateMachine.Gherkin.Lexical;

internal sealed record LxText(string Text, int Line) : LxValue(Line);
