using System.Globalization;
using Trax.Api.DTOs;
using Trax.Scheduler.Services.Operations;

namespace Trax.Api.Services.BulkOperations;

/// <summary>
/// One bulk operation's state on the node running it: the ids it snapshot, how far it has got,
/// and what did not take. <see cref="BulkOperationJobs"/> writes it from the batch loop; every
/// read takes a consistent copy under its lock.
/// </summary>
internal sealed class BulkOperationRun
{
    /// <summary>
    /// The most failures kept with their id. Past this they are only counted, by reason, so an
    /// operation over millions of rows that mostly fail holds a bounded amount of memory.
    /// </summary>
    public const int MaxFailureDetail = 10_000;

    private readonly Lock _gate = new();
    private readonly List<BulkOperationFailure> _failures = [];
    private readonly Dictionary<string, int> _reasons = new(StringComparer.Ordinal);
    private BulkOperationStatus _status = BulkOperationStatus.Running;
    private int _done;
    private int _failed;
    private DateTime? _finishedAt;
    private string _message;
    private BulkOperationSnapshot? _snapshot;

    public BulkOperationRun(
        Guid id,
        BulkOperationRequest request,
        BulkOperationActor actor,
        DateTime startedAt,
        BulkOperationSnapshot snapshot
    )
    {
        Id = id;
        Kind = request.Kind;
        Filter = request.Filter;
        AskAfresh = request.AskAfresh;
        Note = request.Note;
        Actor = actor;
        StartedAt = startedAt;
        Total = snapshot.Ids.Length;
        _snapshot = snapshot;
        _message = string.Create(
            CultureInfo.InvariantCulture,
            $"{Describe(Kind)} {Total} {Noun(Kind)}."
        );
    }

    public Guid Id { get; }
    public BulkOperationKind Kind { get; }
    public BulkOperationFilter Filter { get; }
    public bool AskAfresh { get; }
    public string? Note { get; }
    public BulkOperationActor Actor { get; }
    public DateTime StartedAt { get; }
    public int Total { get; }

    /// <summary>The longest any one batch took, for the stress suite.</summary>
    public TimeSpan LongestBatch { get; private set; }

    /// <summary>How many batches have run, for the stress suite.</summary>
    public int Batches { get; private set; }

    public DateTime? FinishedAt
    {
        get
        {
            lock (_gate)
                return _finishedAt;
        }
    }

    /// <summary>
    /// The ids to attempt and where each batch ends, released once the operation stops so a
    /// finished operation keeps only its failures.
    /// </summary>
    public (long[] Ids, int[] Ends) Plan =>
        _snapshot is { } snapshot ? (snapshot.Ids, snapshot.BatchEnds) : ([], []);

    /// <summary>The operation as <c>bulkOperation</c> reads it.</summary>
    public BulkOperation Read()
    {
        lock (_gate)
            return new BulkOperation(
                this,
                new BulkOperationProgress(
                    _status,
                    _done,
                    _failed,
                    _finishedAt,
                    _message,
                    _reasons
                        .OrderByDescending(r => r.Value)
                        .ThenBy(r => r.Key, StringComparer.Ordinal)
                        .Select(r => new BulkOperationFailureReason(r.Key, r.Value))
                        .ToList()
                )
            );
    }

    /// <summary>A page of failures in id order; <see cref="Record"/> keeps them sorted.</summary>
    public IReadOnlyList<BulkOperationFailure> Failures(int take, long? afterId)
    {
        take = Math.Clamp(take, 1, OperationsService.MaxPageSize);
        lock (_gate)
        {
            // The failures are in ascending id order, so the page starts at the first id past
            // the cursor.
            var start = afterId is { } after ? InsertionPoint(after) : 0;
            return _failures.Skip(start).Take(take).ToList();
        }
    }

    /// <summary>Records one batch: how many ids it ran on, and those that did not take.</summary>
    public void Record(int attempted, IReadOnlyList<BulkOperationFailure> failures, TimeSpan took)
    {
        lock (_gate)
        {
            _done += attempted - failures.Count;
            _failed += failures.Count;
            foreach (var failure in failures)
            {
                if (_failures.Count < MaxFailureDetail)
                    _failures.Insert(InsertionPoint(failure.Id), failure);
                _reasons[failure.Reason] = _reasons.GetValueOrDefault(failure.Reason) + 1;
            }
            Batches++;
            if (took > LongestBatch)
                LongestBatch = took;
            _message = string.Create(
                CultureInfo.InvariantCulture,
                $"{Describe(Kind)} {Total} {Noun(Kind)}: {_done} done and {_failed} failed so far."
            );
        }
    }

    /// <summary>Where a failure with this id goes to keep the list in id order.</summary>
    private int InsertionPoint(long id)
    {
        int lo = 0,
            hi = _failures.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (_failures[mid].Id <= id)
                lo = mid + 1;
            else
                hi = mid;
        }
        return lo;
    }

    /// <summary>Stops the operation; a message of <see langword="null"/> describes how it finished.</summary>
    public BulkOperationOutcome Finish(BulkOperationStatus status, string? message, DateTime at)
    {
        lock (_gate)
        {
            _status = status;
            _finishedAt = at;
            _message =
                message
                ?? string.Create(
                    CultureInfo.InvariantCulture,
                    $"{Past(Kind)} {_done} of {Total} {Noun(Kind)}; {_failed} did not take."
                );
            _snapshot = null;
            return new BulkOperationOutcome(
                Id,
                Kind,
                status,
                Actor,
                StartedAt,
                at,
                Total,
                _done,
                _failed,
                _message
            );
        }
    }

    private static string Describe(BulkOperationKind kind) =>
        kind switch
        {
            BulkOperationKind.Cancel => "Requesting cancellation of",
            BulkOperationKind.Requeue => "Requeueing",
            BulkOperationKind.Acknowledge => "Acknowledging",
            BulkOperationKind.Trigger => "Triggering",
            BulkOperationKind.Enable => "Enabling",
            BulkOperationKind.Disable => "Disabling",
            _ => kind.ToString(),
        };

    private static string Past(BulkOperationKind kind) =>
        kind switch
        {
            BulkOperationKind.Cancel => "Requested cancellation of",
            BulkOperationKind.Requeue => "Requeued",
            BulkOperationKind.Acknowledge => "Acknowledged",
            BulkOperationKind.Trigger => "Triggered",
            BulkOperationKind.Enable => "Enabled",
            BulkOperationKind.Disable => "Disabled",
            _ => kind.ToString(),
        };

    /// <summary>What the kind's ids are, plural.</summary>
    public static string Noun(BulkOperationKind kind) =>
        kind switch
        {
            BulkOperationKind.Cancel => "run(s)",
            BulkOperationKind.Requeue or BulkOperationKind.Acknowledge => "dead letter(s)",
            _ => "manifest(s)",
        };
}
