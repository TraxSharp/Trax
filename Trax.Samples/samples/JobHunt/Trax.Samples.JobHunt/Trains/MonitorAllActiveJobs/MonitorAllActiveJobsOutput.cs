namespace Trax.Samples.JobHunt.Trains.MonitorAllActiveJobs;

public record MonitorAllActiveJobsOutput
{
    public int JobsChecked { get; init; }
    public int JobsChanged { get; init; }
    public int JobsClosed { get; init; }
}
