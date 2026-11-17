namespace Trax.StateMachine.Gherkin.Lexical;

internal sealed class LxGuard(string name, LxRule rule, int line)
{
    internal string Name { get; } = name;
    internal LxRule Rule { get; } = rule;
    internal int Line { get; } = line;
    internal string? Holds { get; set; }
    internal int HoldsLine { get; set; }
    internal string? Fails { get; set; }
    internal int FailsLine { get; set; }
}
