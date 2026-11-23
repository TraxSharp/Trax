namespace Trax.StateMachine.Gherkin.Lexical;

/// <summary>
/// A field: <c>name is a type</c> with its clauses, or, for a context field something writes, only
/// <c>name may be empty</c> (<see cref="Type"/> is null).
/// </summary>
internal sealed record LxField(
    string Name,
    LxTypeRef? Type,
    bool MayBeEmpty,
    double? Minimum,
    double? Maximum,
    IReadOnlyList<string>? Enum,
    LxRule? Where,
    int Line
);
