using System.Globalization;
using Trax.Api.DTOs;
using Trax.Scheduler.Services.Operations;

namespace Trax.Api.Services.BulkOperations;

/// <summary>How a bulk operation ended, as an observer such as the audit sees it.</summary>
internal sealed record BulkOperationOutcome(
    Guid Id,
    BulkOperationKind Kind,
    BulkOperationStatus Status,
    BulkOperationActor Actor,
    DateTime StartedAt,
    DateTime FinishedAt,
    int Total,
    int Done,
    int Failed,
    string Message
);
