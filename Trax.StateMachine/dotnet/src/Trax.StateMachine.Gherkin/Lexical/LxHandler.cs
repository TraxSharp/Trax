namespace Trax.StateMachine.Gherkin.Lexical;

internal sealed record LxHandler(
    string Name,
    string Type,
    bool? RuleExample,
    IReadOnlyList<LxAssign>? ReductionExample,
    int Line
);
