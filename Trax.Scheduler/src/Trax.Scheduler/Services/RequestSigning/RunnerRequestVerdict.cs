using System.Buffers;
using System.Collections.Concurrent;
using System.ComponentModel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using Trax.Scheduler.Configuration;

namespace Trax.Scheduler.Services.RequestSigning;

/// <summary>
/// The outcome of <see cref="RunnerRequestVerifier.VerifyAsync"/>.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/scheduler-api/add-trax-job-runner#infrastructure-types">SDK reference</seealso>
public enum RunnerRequestVerdict
{
    /// <summary>The request may run.</summary>
    Accepted,

    /// <summary>A signing key is configured and the request carries no signature.</summary>
    Missing,

    /// <summary>The signature is malformed or does not match the body.</summary>
    Invalid,

    /// <summary>The signature's timestamp is further from this clock than the allowed skew.</summary>
    Stale,

    /// <summary>The signature's nonce was already accepted inside the skew window.</summary>
    Replayed,
}
