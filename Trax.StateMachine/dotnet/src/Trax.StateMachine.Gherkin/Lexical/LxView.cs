namespace Trax.StateMachine.Gherkin.Lexical;

internal sealed class LxView(string name, int line)
{
    internal string Name { get; } = name;
    internal int Line { get; } = line;
    internal List<string>? Reads { get; set; }
    internal int ReadsLine { get; set; }
    internal List<LxViewField> Fields { get; } = [];
}
