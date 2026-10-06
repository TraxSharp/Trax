using Microsoft.Extensions.Options;

namespace Trax.Api.GraphQL.Audit;

/// <summary>
/// Refuses <see cref="TraxAuditOptions"/> the pipeline cannot run with. <c>AddAudit</c> registers
/// it with <c>ValidateOnStart</c>, so a host configured with, say, <c>BatchSize = 0</c> fails while
/// starting, naming the option, instead of spinning a writer that never writes.
/// </summary>
/// <remarks>
/// This is the options pattern's own startup validation, which the host runs before any hosted
/// service starts, so it gives the guarantee
/// <c>docs/adr/0001-a-misconfigured-host-fails-at-startup.md</c> asks for.
/// </remarks>
internal sealed class TraxAuditOptionsValidator : IValidateOptions<TraxAuditOptions>
{
    /// <summary>The most retries a batch may be given.</summary>
    internal const int MaxRetriesLimit = 100;

    /// <summary>The longest <see cref="TraxAuditOptions.MaxRetryBackoff"/> may be.</summary>
    internal static readonly TimeSpan MaxRetryBackoffLimit = TimeSpan.FromHours(1);

    public ValidateOptionsResult Validate(string? name, TraxAuditOptions options)
    {
        var failures = new List<string>();

        Positive(failures, nameof(options.ChannelCapacity), options.ChannelCapacity);
        Positive(failures, nameof(options.BatchSize), options.BatchSize);
        Positive(failures, nameof(options.MaxDocumentLength), options.MaxDocumentLength);
        Positive(failures, nameof(options.MaxOperationNameLength), options.MaxOperationNameLength);
        Positive(failures, nameof(options.MaxErrorTextLength), options.MaxErrorTextLength);

        if (options.FlushInterval <= TimeSpan.Zero)
            failures.Add(
                Message(
                    nameof(options.FlushInterval),
                    "must be greater than zero",
                    options.FlushInterval
                )
            );

        if (options.MaxRetries is < 0 or > MaxRetriesLimit)
            failures.Add(
                Message(
                    nameof(options.MaxRetries),
                    $"must be from 0 to {MaxRetriesLimit}",
                    options.MaxRetries
                )
            );

        if (
            options.MaxRetryBackoff <= TimeSpan.Zero
            || options.MaxRetryBackoff > MaxRetryBackoffLimit
        )
            failures.Add(
                Message(
                    nameof(options.MaxRetryBackoff),
                    $"must be greater than zero and at most {MaxRetryBackoffLimit}",
                    options.MaxRetryBackoff
                )
            );

        if (options.RetryBackoff < TimeSpan.Zero || options.RetryBackoff > options.MaxRetryBackoff)
            failures.Add(
                Message(
                    nameof(options.RetryBackoff),
                    $"must be zero or more and no greater than MaxRetryBackoff ({options.MaxRetryBackoff})",
                    options.RetryBackoff
                )
            );

        if (string.IsNullOrEmpty(options.DefaultPrincipalId))
            failures.Add(Message(nameof(options.DefaultPrincipalId), "must not be empty", "\"\""));

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void Positive(List<string> failures, string option, int value)
    {
        if (value <= 0)
            failures.Add(Message(option, "must be greater than 0", value));
    }

    private static string Message(string option, string rule, object? value) =>
        $"TraxAuditOptions.{option} {rule}; it is {value}. Set it in AddAudit(options => ...).";
}
