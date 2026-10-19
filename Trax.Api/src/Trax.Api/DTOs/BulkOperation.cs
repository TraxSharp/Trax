using Trax.Api.Services.BulkOperations;

namespace Trax.Api.DTOs;

/// <summary>What a bulk operation does to each id it matched.</summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/graphql-api/mutations/bulk-operations">SDK reference</seealso>
public enum BulkOperationKind
{
    /// <summary>Requests cancellation of each run, as <c>cancelExecutions</c> does.</summary>
    Cancel,

    /// <summary>Requeues each dead letter, as <c>requeueDeadLetters</c> does.</summary>
    Requeue,

    /// <summary>Acknowledges each dead letter with the operation's note, as <c>acknowledgeDeadLetters</c> does.</summary>
    Acknowledge,

    /// <summary>Triggers each manifest, as <c>triggerManifests</c> does.</summary>
    Trigger,

    /// <summary>Enables each manifest, as <c>setManifestsEnabled(enabled: true)</c> does.</summary>
    Enable,

    /// <summary>Disables each manifest, as <c>setManifestsEnabled(enabled: false)</c> does.</summary>
    Disable,
}

/// <summary>Where a bulk operation is.</summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/graphql-api/mutations/bulk-operations">SDK reference</seealso>
public enum BulkOperationStatus
{
    /// <summary>Batches are still running.</summary>
    Running,

    /// <summary>
    /// Every id it matched was attempted. <c>failed</c> counts the ones that
    /// did not take, each with its reason.
    /// </summary>
    Finished,

    /// <summary>
    /// It stopped before the end: the server refused a batch (the message says why) or failed
    /// (the detail is in the server's log). The ids it had not reached were not attempted.
    /// </summary>
    Failed,

    /// <summary>The server was shutting down, so it stopped between batches.</summary>
    Canceled,
}

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
    /// <c>true</c> when every run it requeues or triggers asks its deciders afresh rather than
    /// replaying recorded decisions.
    /// </summary>
    public bool AskAfresh => _run.AskAfresh;

    /// <summary>When it started (UTC).</summary>
    public DateTime StartedAt => _run.StartedAt;

    /// <summary>When it stopped (UTC), or <c>null</c> while running.</summary>
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

/// <summary>One id a bulk operation did not apply to, and why.</summary>
/// <param name="Id">The run, dead letter or manifest id.</param>
/// <param name="Reason">Why it did not take, in the operator's words.</param>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/graphql-api/mutations/bulk-operations">SDK reference</seealso>
public sealed record BulkOperationFailure(long Id, string Reason);

/// <summary>One reason a bulk operation's ids did not take, and how many.</summary>
/// <param name="Reason">The reason.</param>
/// <param name="Count">How many ids failed for it.</param>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/graphql-api/mutations/bulk-operations">SDK reference</seealso>
public sealed record BulkOperationFailureReason(string Reason, int Count);

/// <summary>
/// The answer to <c>startBulkOperation</c>. A refusal starts nothing: <c>success</c> is
/// <c>false</c> and <c>message</c> says why. When the filter matched a different number of
/// ids than the caller confirmed, <c>matched</c> is the number it matches now, to confirm
/// and start again. While another bulk operation runs on this node, <c>operation</c> is
/// that one.
/// </summary>
/// <param name="Success">Whether an operation was started.</param>
/// <param name="Message">What happened, for an operator.</param>
/// <param name="Matched">How many ids the filter matched, or <c>null</c> when it was not read.</param>
/// <param name="Operation">The started operation, or the one already running; else <c>null</c>.</param>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/graphql-api/mutations/bulk-operations">SDK reference</seealso>
public sealed record StartBulkOperationResponse(
    bool Success,
    string Message,
    int? Matched,
    BulkOperation? Operation
);
