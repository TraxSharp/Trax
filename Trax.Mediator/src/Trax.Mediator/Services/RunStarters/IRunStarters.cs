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
}
