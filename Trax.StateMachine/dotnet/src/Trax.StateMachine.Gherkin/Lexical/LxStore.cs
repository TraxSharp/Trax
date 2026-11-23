namespace Trax.StateMachine.Gherkin.Lexical;

/// <summary>An output field a <c>Stores:</c> line keeps, under its own name or another.</summary>
internal sealed record LxStore(string Output, string Context, int Line);
