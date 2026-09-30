using Trax.Effect.Models.Metadata;

namespace Trax.Effect.Services.EffectProvider;

/// <summary>
/// Implemented by an effect provider that stores runs, so a run started from a caller-supplied
/// <c>Pending</c> row claims that row in the store before its body runs. The data providers
/// implement it; other effects do not need to.
/// </summary>
/// <remarks>
/// Two executions handed the same row each hold a copy that says <c>Pending</c>, so a check of
/// that copy passes for both. The claim is decided by the store in one conditional write, which
/// moves the row to <c>InProgress</c> only while it is still <c>Pending</c>.
/// </remarks>
public interface IPendingRunClaim
{
    /// <summary>
    /// Moves the stored row of <paramref name="metadata"/> from <c>Pending</c> to
    /// <c>InProgress</c> if, and only if, it is still <c>Pending</c>, and writes that at once rather
    /// than on the next save.
    /// </summary>
    /// <param name="metadata">A run whose row already exists in the store.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>
    /// <c>false</c> only when the row is stored and no longer <c>Pending</c>, because another
    /// execution started it. <c>true</c> when this call moved the row, and when the row is not
    /// stored (a caller may hand a run a row it has not saved; that row is the run's own).
    /// </returns>
    Task<bool> TryClaimPendingRun(Metadata metadata, CancellationToken cancellationToken);
}
