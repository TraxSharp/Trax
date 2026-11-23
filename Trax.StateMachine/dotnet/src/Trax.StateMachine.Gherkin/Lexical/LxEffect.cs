namespace Trax.StateMachine.Gherkin.Lexical;

internal sealed record LxEffect(string Type, string? KeyPrefix, string Example, int Line);
