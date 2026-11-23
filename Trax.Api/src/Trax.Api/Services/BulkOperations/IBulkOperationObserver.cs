using System.Globalization;
using Trax.Api.DTOs;
using Trax.Scheduler.Services.Operations;

namespace Trax.Api.Services.BulkOperations;

/// <summary>
/// Told when a bulk operation on this node stops. The API's audit registers one when the host
/// calls <c>AddAudit</c>, so the outcome lands in the audit trail beside the request that started
/// it. An observer must not throw or block; one that throws is logged and skipped.
/// </summary>
internal interface IBulkOperationObserver
{
    void Finished(BulkOperationOutcome outcome);
}
