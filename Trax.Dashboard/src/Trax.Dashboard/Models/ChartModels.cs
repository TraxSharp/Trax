namespace Trax.Dashboard.Models;

public class ExecutionTimePoint
{
    public string Hour { get; init; } = "";
    public int Completed { get; init; }
    public int Failed { get; init; }
    public int Cancelled { get; init; }
}

public class StateCount
{
    public string State { get; init; } = "";
    public int Count { get; init; }
}

public class WorkflowFailureCount
{
    public string Name { get; init; } = "";
    public int Count { get; init; }
}

public class WorkflowDuration
{
    public string Name { get; init; } = "";
    public double AvgMs { get; init; }
}
