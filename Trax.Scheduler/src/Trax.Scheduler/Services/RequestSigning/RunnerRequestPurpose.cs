using System.ComponentModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Trax.Scheduler.Services.RequestSigning;

/// <summary>
/// What a signed runner request is for. Part of the signed text, so a signature made for one
/// purpose does not verify for the other.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/scheduler-api/add-trax-job-runner#infrastructure-types">SDK reference</seealso>
public enum RunnerRequestPurpose
{
    /// <summary>A queued job: <c>/trax/execute</c>, a Lambda <c>Execute</c> envelope, or an SQS message.</summary>
    Execute,

    /// <summary>A synchronous run: <c>/trax/run</c> or a Lambda <c>Run</c> envelope.</summary>
    Run,
}
