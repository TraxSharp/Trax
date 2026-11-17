namespace Trax.StateMachine.Gherkin.Lexical;

internal sealed record LxBool(bool Value, int Line) : LxValue(Line);
