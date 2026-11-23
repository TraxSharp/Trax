namespace Trax.StateMachine.Gherkin.Lexical;

internal sealed class LxType(string name, int line)
{
    internal string Name { get; } = name;
    internal int Line { get; } = line;
    internal List<string>? Values { get; set; }
    internal int ValuesLine { get; set; }
    internal List<LxField> Has { get; } = [];
}
