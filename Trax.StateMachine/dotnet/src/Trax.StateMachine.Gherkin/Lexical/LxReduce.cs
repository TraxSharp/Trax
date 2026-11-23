namespace Trax.StateMachine.Gherkin.Lexical;

internal sealed record LxReduce(string Kind, IReadOnlyList<LxSet>? Steps, string? Name, int Line);
