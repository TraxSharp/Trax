namespace Trax.StateMachine.Gherkin.Lexical;

internal sealed class LxState(string name, string phrase, bool committed, int line)
{
    internal string Name { get; } = name;
    internal string Phrase { get; } = phrase;
    internal bool Committed { get; } = committed;
    internal int Line { get; } = line;
    internal LxRule? Requires { get; set; }
    internal List<LxField>? Context { get; set; }
}
