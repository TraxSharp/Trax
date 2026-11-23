namespace Trax.Scheduler.Services.Deploy;

/// <summary>Marks the endpoints that receive work from a remote scheduler.</summary>
internal sealed class JobRunnerEndpointMetadata
{
    public static readonly JobRunnerEndpointMetadata Instance = new();

    private JobRunnerEndpointMetadata() { }
}
