namespace Trax.Scheduler.Services.Operations;

/// <summary>
/// Generic result envelope for <see cref="IOperationsService"/> calls.
/// Failures are returned as <c>OperationResult(false, ..., Message)</c> rather than
/// thrown so both the GraphQL layer and the dashboard can surface user-facing errors
/// without try/catch noise.
/// </summary>
/// <param name="Success">Whether the operation succeeded.</param>
/// <param name="Id">
/// For create/cancel operations, the affected entity's database ID. Null when no row was
/// touched (e.g. on validation failure).
/// </param>
/// <param name="Count">Number of rows affected, when meaningful (batch operations).</param>
/// <param name="Message">Human-readable explanation. Always populated on failure.</param>
public record OperationResult(
    bool Success,
    long? Id = null,
    int? Count = null,
    string? Message = null
);
