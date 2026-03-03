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

public class TrainFailureCount
{
    public string Name { get; init; } = "";
    public int Count { get; init; }
}

public class TrainDuration
{
    public string Name { get; init; } = "";
    public double AvgMs { get; init; }
}

public class ThroughputMetric
{
    public string Minute { get; init; } = "";
    public int Completed { get; init; }
    public int Failed { get; init; }
}
