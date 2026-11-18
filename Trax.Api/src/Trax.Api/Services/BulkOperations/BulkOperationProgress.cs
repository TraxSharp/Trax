using System.Globalization;
using Trax.Api.DTOs;
using Trax.Scheduler.Services.Operations;

namespace Trax.Api.Services.BulkOperations;

/// <summary>The moving part of a bulk operation, read together under its lock.</summary>
internal sealed record BulkOperationProgress(
    BulkOperationStatus Status,
    int Done,
    int Failed,
    DateTime? FinishedAt,
    string Message,
    IReadOnlyList<BulkOperationFailureReason> Reasons
);
