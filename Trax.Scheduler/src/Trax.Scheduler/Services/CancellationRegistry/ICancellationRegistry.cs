namespace Trax.Scheduler.Services.CancellationRegistry;

/// <summary>
/// Tracks in-flight workflow CancellationTokenSources by metadata ID so that
/// running workflows can be cancelled from the dashboard or other external callers.
/// </summary>
public interface ICancellationRegistry
{
    /// <summary>
    /// Registers a CancellationTokenSource for the given metadata ID.
    /// </summary>
    void Register(long metadataId, CancellationTokenSource cts);

    /// <summary>
    /// Removes the CancellationTokenSource for the given metadata ID.
    /// Does NOT dispose the CTS — the caller owns its lifetime.
    /// </summary>
    void Unregister(long metadataId);

    /// <summary>
    /// Attempts to cancel the workflow with the given metadata ID.
    /// Returns true if the CTS was found and Cancel() was called.
    /// </summary>
    bool TryCancel(long metadataId);
}
