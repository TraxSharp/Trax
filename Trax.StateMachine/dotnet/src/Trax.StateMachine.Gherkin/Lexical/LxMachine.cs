namespace Trax.StateMachine.Gherkin.Lexical;

internal sealed class LxMachine(string id, string? noun, int line)
{
    internal string Id { get; } = id;
    internal string? Noun { get; } = noun;
    internal int Line { get; } = line;
    internal List<string>? OwnedBy { get; set; }
    internal int OwnedLine { get; set; }

    /// <summary>The line of <c>System</c>, <c>System at:</c> or <c>System keyed by:</c>, or 0.</summary>
    internal int SystemLine { get; set; }
    internal string? SystemAt { get; set; }
    internal List<string>? KeyedBy { get; set; }
    internal string? Start { get; set; }
    internal int StartLine { get; set; }
    internal string? Initial { get; set; }
    internal int InitialLine { get; set; }
    internal int? Version { get; set; }
    internal int VersionLine { get; set; }
    internal List<LxMigration> Migrations { get; } = [];
    internal int DifferentialLine { get; set; }
    internal List<LxAssign>? StartsWith { get; set; }
    internal int StartsWithLine { get; set; }
    internal List<LxField> Context { get; } = [];
    internal List<LxState> States { get; } = [];
    internal List<LxTrigger> Triggers { get; } = [];
    internal List<LxGuard> Guards { get; } = [];
    internal List<LxRuns> Runs { get; } = [];
    internal List<LxLink> Links { get; } = [];
    internal List<LxHandler> Rules { get; } = [];
    internal List<LxHandler> Reductions { get; } = [];
}
