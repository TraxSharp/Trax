namespace Trax.StateMachine.Gherkin.Lexical;

internal sealed class LxTrigger(string name, string phrase, int line)
{
    internal string Name { get; } = name;
    internal string Phrase { get; } = phrase;
    internal int Line { get; } = line;
    internal List<LxField> Input { get; } = [];
    internal LxReduce? Reduce { get; set; }
    internal string? Guard { get; set; }
    internal int GuardLine { get; set; }
    internal LxEffect? Effect { get; set; }
}
