using Trax.Api.Services.BulkOperations;

namespace Trax.Api.DTOs;

/// <summary>
/// One action applied to every id a filter matched, run in the background in batches on the node
/// that started it. <c>startBulkOperation</c> returns one at once, and
/// <c>operations.bulkOperation(id)</c> reads it again until <c>status</c> is no longer
/// <c>RUNNING</c>.
/// </summary>
/// <remarks>
/// The ids are the ones the filter matched when the operation started, and only those: a row that
/// starts matching afterwards is not touched. Each batch runs through the same shared operation the
/// id-list mutation of its kind calls, in its own short transaction, so an operation that stops
/// part-way leaves the batches it finished applied and the rest untouched.
/// </remarks>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/graphql-api/mutations/bulk-operations">SDK reference</seealso>
public sealed class BulkOperation
{
    private readonly BulkOperationRun _run;

    internal BulkOperation(BulkOperationRun run, BulkOperationProgress progress)
    {
        _run = run;
        Status = progress.Status;
        Done = progress.Done;
        Failed = progress.Failed;
        FinishedAt = progress.FinishedAt;
        Message = progress.Message;
        FailureReasons = progress.Reasons;
    }

    /// <summary>Identifies the operation to <c>bulkOperation</c> on the node that started it.</summary>
    public Guid Id => _run.Id;

    /// <summary>What it does to each id.</summary>
    public BulkOperationKind Kind => _run.Kind;

    /// <summary>The filter it was started with, as it was given.</summary>
    public BulkOperationFilter Filter => _run.Filter;

    /// <summary>
    /// Who started it: the caller's principal id (<c>{scheme}:{id}</c>), else the name the
    /// caller signed in with, else <c>&lt;anonymous&gt;</c>.
    /// </summary>
    public string Actor => _run.Actor.Display;

    /// <summary>
    /// <see langword="true"/> when every run it requeues or triggers asks its deciders afresh rather than
    /// replaying recorded decisions.
    /// </summary>
    public bool AskAfresh => _run.AskAfresh;

    /// <summary>When it started (UTC).</summary>
    public DateTime StartedAt => _run.StartedAt;

    /// <summary>When it stopped (UTC), or <see langword="null"/> while running.</summary>
    public DateTime? FinishedAt { get; }

    /// <summary>Where it is.</summary>
    public BulkOperationStatus Status { get; }

    /// <summary>How many ids the filter matched when it started, every one it will attempt.</summary>
    public int Total => _run.Total;

    /// <summary>
    /// How many ids are in the state the operation asks for once their batch ran: cancellation
    /// requested, requeued, acknowledged, triggered, enabled or disabled.
    /// </summary>
    public int Done { get; }

    /// <summary>How many ids their batch ran on and did not take; each has a reason.</summary>
    public int Failed { get; }

    /// <summary>A human-readable description of where it is or how it ended.</summary>
    public string Message { get; }

    /// <summary>Every failure, grouped by its reason, the most common first.</summary>
    public IReadOnlyList<BulkOperationFailureReason> FailureReasons { get; }

    /// <summary>
    /// The ids that did not take, in id order, each with its reason. Pass the last id of a page as
    /// <paramref name="afterId"/> for the next. The first
    /// 10,000 failures are kept; past that they
    /// are counted in <c>failed</c> and <c>failureReasons</c> only.
    /// </summary>
    /// <param name="take">The page size, clamped to 1 through 500.</param>
    /// <param name="afterId">Only failures with a larger id (the previous page's last id).</param>
    public IReadOnlyList<BulkOperationFailure> GetFailures(int take = 100, long? afterId = null) =>
        _run.Failures(take, afterId);
}
