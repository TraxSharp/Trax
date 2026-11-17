namespace Trax.StateMachine.Gherkin.Lexical;

/// <summary>A rule: one atom, or several joined by one connective (<c>and</c> or <c>or</c>, never both).</summary>
internal sealed record LxRule(IReadOnlyList<LxAtom> Atoms, string? Connective, int Line);
