namespace Trax.StateMachine.Gherkin.Lexical;

internal sealed class LxActor(string name, string noun, int line)
{
    internal string Name { get; } = name;
    internal string Noun { get; } = noun;
    internal int Line { get; } = line;
    internal List<string>? Roles { get; set; }
    internal int RolesLine { get; set; }
    internal int AnyLine { get; set; }
}
