namespace Trax.StateMachine.Gherkin.Lexical;

/// <summary>One comparison of a rule, as written: <c>loaves is at least 1</c>, <c>input item is not empty</c>.</summary>
internal sealed record LxAtom(
    string Kind,
    string? Source,
    string Field,
    int Line,
    string? Op = null,
    LxValue? Value = null,
    IReadOnlyList<string>? Values = null,
    string? Type = null,
    string? Name = null
);
