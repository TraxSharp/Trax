namespace Trax.StateMachine.Gherkin.Lexical;

internal sealed class LxRuns(string train, string state, int line)
{
    internal string Train { get; } = train;
    internal string State { get; } = state;
    internal int Line { get; } = line;

    /// <summary>The inputs in the order written, from <c>Takes:</c> and <c>Input:</c> lines alike.</summary>
    internal List<LxInput> Inputs { get; } = [];

    /// <summary>Whether a <c>Takes:</c> line restates the train's inputs.</summary>
    internal bool Takes { get; set; }
    internal List<LxField>? Returns { get; set; }
    internal int ReturnsLine { get; set; }
    internal List<LxStore> Stores { get; } = [];
    internal List<LxSet> Sets { get; } = [];
    internal List<LxAssign> Example { get; } = [];
    internal List<LxOutcome> Outcomes { get; } = [];
}
