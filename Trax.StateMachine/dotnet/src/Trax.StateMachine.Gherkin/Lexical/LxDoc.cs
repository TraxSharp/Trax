namespace Trax.StateMachine.Gherkin.Lexical;

internal sealed class LxDoc
{
    internal int? Version { get; set; }
    internal int VersionLine { get; set; }
    internal string? TrainsIn { get; set; }
    internal int TrainsInLine { get; set; }
    internal List<LxActor> Actors { get; } = [];
    internal List<LxConstant> Constants { get; } = [];
    internal List<LxType> Types { get; } = [];
    internal List<LxMachine> Machines { get; } = [];
    internal List<LxView> Views { get; } = [];
}
