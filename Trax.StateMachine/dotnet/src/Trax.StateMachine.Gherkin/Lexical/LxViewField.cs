namespace Trax.StateMachine.Gherkin.Lexical;

internal sealed class LxViewField(string name, string machine, string source, int line)
{
    internal string Name { get; } = name;
    internal string Machine { get; } = machine;
    internal string Source { get; } = source;
    internal int Line { get; } = line;
    internal List<LxLabel> Labels { get; } = [];
}
