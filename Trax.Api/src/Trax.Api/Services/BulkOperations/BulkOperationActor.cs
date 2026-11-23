using System.Globalization;
using Trax.Api.DTOs;
using Trax.Scheduler.Services.Operations;

namespace Trax.Api.Services.BulkOperations;

/// <summary>
/// Who started a bulk operation. <paramref name="Display"/> is what <c>actor</c> reads;
/// <paramref name="PrincipalId"/> and <paramref name="PrincipalType"/> are the caller's Trax
/// principal claims, when it carried them, for the audit entry.
/// </summary>
internal sealed record BulkOperationActor(
    string Display,
    string? PrincipalId,
    string? PrincipalType
)
{
    /// <summary>What <c>actor</c> reads for a caller with no identity.</summary>
    public const string Anonymous = "<anonymous>";
}
