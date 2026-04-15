namespace Trax.Api.GraphQL.Audit;

/// <summary>
/// Tunable options for the Trax GraphQL audit pipeline.
/// </summary>
/// <remarks>
/// NO WARRANTY. Trax auth is plumbing, not a security product. You are solely
/// responsible for securing systems that use it. See SECURITY-DISCLAIMER.md.
/// </remarks>
public sealed class TraxAuditOptions
{
    /// <summary>Bounded channel capacity. When full, new entries are dropped and the drop meter is incremented.</summary>
    public int ChannelCapacity { get; set; } = 10_000;

    /// <summary>Maximum batch size handed to the sink.</summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>How long the writer waits for a batch to fill before flushing a partial batch.</summary>
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Documents longer than this are truncated with a trailing "...[truncated]" marker.</summary>
    public int MaxDocumentLength { get; set; } = 65_536;

    /// <summary>Skip introspection queries (<c>__schema</c>, <c>__type</c>, operation name <c>IntrospectionQuery</c>).</summary>
    public bool SkipIntrospection { get; set; } = true;

    /// <summary>Skip subscription operations. They don't fit a request/response audit model well.</summary>
    public bool SkipSubscriptions { get; set; } = true;

    /// <summary>PrincipalId used when the request has no Trax principal claim.</summary>
    public string DefaultPrincipalId { get; set; } = "<anonymous>";

    /// <summary>How many times the writer retries a failing sink batch before dropping it.</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>Initial backoff between sink retries. Multiplied on each attempt.</summary>
    public TimeSpan RetryBackoff { get; set; } = TimeSpan.FromMilliseconds(100);
}
