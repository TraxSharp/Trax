using System.Globalization;
using Trax.Api.DTOs;
using Trax.Scheduler.Services.Operations;

namespace Trax.Api.Services.BulkOperations;

/// <summary>What a bulk operation was asked to do, once its request has been checked.</summary>
internal sealed record BulkOperationRequest(
    BulkOperationKind Kind,
    BulkOperationFilter Filter,
    int ExpectedCount,
    bool AskAfresh,
    string? Note
);
