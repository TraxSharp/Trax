namespace Trax.Api.GraphQL.Audit;

/// <summary>
/// Tunable options for the Trax GraphQL audit pipeline.
/// </summary>
/// <remarks>
/// NO WARRANTY. Trax auth is plumbing, not a security product. You are solely
/// responsible for securing systems that use it. See SECURITY-DISCLAIMER.md.
/// <para>
/// <c>AddAudit</c> validates these when the host starts: a value out of range refuses startup
/// with an <see cref="Microsoft.Extensions.Options.OptionsValidationException"/> naming the
/// option, rather than writing nothing or failing on every request.
/// </para>
/// </remarks>
public sealed class TraxAuditOptions
{
    /// <summary>
    /// Bounded channel capacity. When full, new entries are dropped and the drop meter is
    /// incremented. Must be greater than 0.
    /// </summary>
    public int ChannelCapacity { get; set; } = 10_000;

    /// <summary>Maximum batch size handed to the sink. Must be greater than 0.</summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>
    /// How long the writer waits for a batch to fill before flushing a partial batch. Must be
    /// greater than zero.
    /// </summary>
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Documents longer than this are cut to this many characters, marked "...[truncated]", and
    /// followed by "[selected fields: ...]": every <c>Type.field</c> the operation selects, read
    /// from the compiled operation, so padding ahead of the fields that matter cannot hide them.
    /// The list names each schema coordinate once, so it is bounded by the schema, not the request.
    /// Must be greater than 0.
    /// </summary>
    public int MaxDocumentLength { get; set; } = 65_536;

    /// <summary>
    /// Operation names longer than this are cut to this many characters and marked
    /// "...[truncated]". The name is whatever the caller sent, so without a limit one request
    /// could put megabytes into the channel and the store. Must be greater than 0.
    /// </summary>
    public int MaxOperationNameLength { get; set; } = 256;

    /// <summary>
    /// Error text longer than this is cut to this many characters and marked "...[truncated]".
    /// A request can raise one error per field it selects, so the joined text grows with the
    /// request. Must be greater than 0.
    /// </summary>
    public int MaxErrorTextLength { get; set; } = 4_096;

    /// <summary>
    /// Record the error messages of a failed request in <see cref="TraxAuditEntry.ErrorText"/>.
    /// Off by default: a message is written by HotChocolate or a resolver and can quote what the
    /// caller sent (a coercion error naming the value, a resolver putting its input in an
    /// exception), so by default each error is recorded as its code and path, as
    /// <c>CODE at /path</c>, with <c>&lt;masked&gt;</c> for an error with no code, and a
    /// request-level exception as its type name. Turn it on only when the sink is cleared to hold
    /// what callers send.
    /// </summary>
    public bool RecordErrorMessages { get; set; }

    /// <summary>
    /// Skip introspection queries that succeeded: operations whose top-level selections are all
    /// <c>__schema</c>, <c>__type</c> or <c>__typename</c>. Decided from the operation that
    /// executed. One that raised an error is audited.
    /// </summary>
    public bool SkipIntrospection { get; set; } = true;

    /// <summary>
    /// Skip subscriptions that were accepted: a subscription that returned a stream of events,
    /// which does not fit a request/response audit model well. A subscription that was refused
    /// or failed when subscribing (authorization refused it, its event source threw, it failed
    /// validation) is audited whatever this is set to. See
    /// <c>docs/adr/0035-a-refused-request-is-always-audited.md</c>.
    /// </summary>
    public bool SkipSubscriptions { get; set; } = true;

    /// <summary>
    /// PrincipalId used when the request has no Trax principal claim. Must not be empty.
    /// </summary>
    public string DefaultPrincipalId { get; set; } = "<anonymous>";

    /// <summary>
    /// How many times the writer retries a failing sink batch before dropping it. From 0 to
    /// 100. The writer is single-threaded, so while it retries one batch the channel fills
    /// behind it.
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Initial backoff between sink retries. Doubled on each attempt, capped at
    /// <see cref="MaxRetryBackoff"/>, and jittered: each wait is between half and all of that
    /// value, so writers on many nodes retrying a shared sink do not retry in step. Zero or more,
    /// and no greater than <see cref="MaxRetryBackoff"/>.
    /// </summary>
    public TimeSpan RetryBackoff { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// The longest the writer waits between two sink retries, however many attempts it has
    /// made. Greater than zero and at most one hour.
    /// </summary>
    public TimeSpan MaxRetryBackoff { get; set; } = TimeSpan.FromSeconds(30);
}
