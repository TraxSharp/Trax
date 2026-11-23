namespace Trax.StateMachine.Gherkin.Lexical;

internal sealed record LxNumber(double Number, int Line) : LxValue(Line);
