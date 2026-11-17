namespace Trax.StateMachine.Gherkin.Lexical;

internal sealed record LxOutcome(string Phrase, string Kind, LxRule? When, int Line);
