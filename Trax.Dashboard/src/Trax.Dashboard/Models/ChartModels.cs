namespace Trax.Dashboard.Models;

public class ExecutionTimePoint
{
    public string Label { get; init; } = "";
    public int Completed { get; init; }
    public int Failed { get; init; }
    public int Cancelled { get; init; }
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

public class ThroughputPoint
{
    public string Label { get; init; } = "";
    public int Count { get; init; }
}

public class ThroughputSeries
{
    public string Name { get; init; } = "";
    public string Color { get; init; } = "";
    public List<ThroughputPoint> Points { get; init; } = [];
}
