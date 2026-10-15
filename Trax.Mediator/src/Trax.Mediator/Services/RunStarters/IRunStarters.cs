namespace Trax.Mediator.Services.RunStarters;

/// <summary>
/// Whether the current caller started a run, as the run records its starter: the principal id
/// <see cref="Principal.ICurrentPrincipalProvider"/> gives, compared with the hash the run's rows hold. For a train that
/// answers about a run only to the caller who started it, as <c>runProgress</c> does
/// (Trax.Docs/adr/0051-only-the-caller-who-started-a-run-may-follow-its-progress.md).
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/graphql-api/run-progress">Run progress</seealso>
public interface IRunStarters
{
    /// <summary>
    /// True when the run whose external id is <paramref name="runId"/>, run or still queued, was started by the current
    /// caller. False for a missing id, another caller's run, a run nobody started directly, and a caller with no
    /// principal id, all alike.
    /// </summary>
    Task<bool> StartedByCurrentCallerAsync(string runId, CancellationToken ct = default);

    /// <summary>
    /// The id of the latest execution recorded under <paramref name="runId"/> that the current caller started, or null
    /// when there is none: a missing id, only other callers' executions, only executions nobody started directly, and
    /// a caller with no principal id, all alike. For a train that reads an execution's own rows, so that another
    /// execution recorded under the same id never answers in its place.
    /// </summary>
    Task<long?> LatestExecutionStartedByCurrentCallerAsync(
        string runId,
        CancellationToken ct = default
    );
}
