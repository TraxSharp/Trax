using HotChocolate.Execution;
using HotChocolate.Execution.Instrumentation;
using HotChocolate.Language;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trax.Api.Auth;

namespace Trax.Api.GraphQL.Audit;

/// <summary>
/// HotChocolate <see cref="ExecutionDiagnosticEventListener"/> that captures
/// per-request audit entries and enqueues them to <see cref="TraxAuditChannel"/>.
/// Non-blocking, swallows all exceptions: a misbehaving sink or redactor must
/// never crash a GraphQL request.
/// </summary>
/// <remarks>
/// NO WARRANTY. Trax auth is plumbing, not a security product. You are solely
/// responsible for securing systems that use it. See SECURITY-DISCLAIMER.md.
/// </remarks>
public sealed class TraxGraphQLAuditListener(
    IHttpContextAccessor httpContextAccessor,
    TraxAuditChannel channel,
    IOptions<TraxAuditOptions> options,
    ITraxAuditRedactor redactor,
    TimeProvider timeProvider,
    ILogger<TraxGraphQLAuditListener> logger
) : ExecutionDiagnosticEventListener
{
    private readonly TraxAuditOptions _options = options.Value;

    /// <inheritdoc />
    public override IDisposable ExecuteRequest(IRequestContext context)
    {
        try
        {
            if (ShouldSkip(context))
                return EmptyScope;

            var startTicks = timeProvider.GetTimestamp();
            var startTime = timeProvider.GetUtcNow();
            var principal = CapturePrincipal();

            return new RequestScope(this, context, startTicks, startTime, principal);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Trax audit listener failed to start capture. Skipping request.");
            return EmptyScope;
        }
    }

    private bool ShouldSkip(IRequestContext context)
    {
        if (_options.SkipIntrospection)
        {
            var opName = context.Request?.OperationName;
            if (string.Equals(opName, "IntrospectionQuery", StringComparison.Ordinal))
                return true;
        }

        if (_options.SkipSubscriptions && context.Operation?.Type == OperationType.Subscription)
            return true;

        return false;
    }

    private (string Id, string? Type) CapturePrincipal()
    {
        var user = httpContextAccessor.HttpContext?.User;
        if (user is null || !user.TryGetPrincipalId(out var id))
            return (_options.DefaultPrincipalId, null);

        var type = user.FindFirst(TraxAuthClaimTypes.PrincipalType)?.Value;
        return (id, type);
    }

    private void CompleteScope(
        IRequestContext context,
        long startTicks,
        DateTimeOffset startTime,
        (string Id, string? Type) principal
    )
    {
        try
        {
            var elapsed = timeProvider.GetElapsedTime(startTicks);
            var document = TruncateDocument(context.Document?.ToString() ?? string.Empty);
            var variables = BuildVariables(context);
            var redactedVariables = SafeRedact(variables);
            var (success, errorText) = InterpretResult(context);

            var entry = new TraxAuditEntry(
                PrincipalId: principal.Id,
                PrincipalType: principal.Type,
                OperationName: context.Request?.OperationName,
                Document: document,
                Variables: redactedVariables,
                DurationMs: (long)elapsed.TotalMilliseconds,
                Timestamp: startTime,
                Success: success,
                ErrorText: errorText,
                Metadata: null
            );

            channel.TryEnqueue(entry);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Trax audit listener failed to build entry. Skipping.");
        }
    }

    private string TruncateDocument(string document)
    {
        if (document.Length <= _options.MaxDocumentLength)
            return document;

        return string.Concat(document.AsSpan(0, _options.MaxDocumentLength), "...[truncated]");
    }

    private static IReadOnlyDictionary<string, object?>? BuildVariables(IRequestContext context)
    {
        var variables = context.Variables;
        if (variables is null)
            return null;

        // context.Variables is IReadOnlyList<IVariableValueCollection> in HC 15.x
        // (one collection per operation, to support batched requests). The inner
        // IVariableValueCollection itself is IEnumerable<VariableValue>, so no
        // cast is needed past the outer list.
        var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var collection in variables)
        {
            if (collection is null)
                continue;
            foreach (var variable in collection)
                dict[variable.Name] = variable.Value?.ToString();
        }
        return dict.Count == 0 ? null : dict;
    }

    private IReadOnlyDictionary<string, object?>? SafeRedact(
        IReadOnlyDictionary<string, object?>? variables
    )
    {
        try
        {
            return redactor.Redact(variables);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Trax audit redactor threw. Dropping variables for safety.");
            return null;
        }
    }

    private static (bool Success, string? ErrorText) InterpretResult(IRequestContext context)
    {
        if (context.Exception is not null)
            return (false, context.Exception.Message);

        if (context.Result is IOperationResult opResult && opResult.Errors is { Count: > 0 } errors)
        {
            var joined = string.Join("; ", errors.Select(e => e.Message));
            return (false, joined);
        }

        return (true, null);
    }

    private sealed class RequestScope(
        TraxGraphQLAuditListener listener,
        IRequestContext context,
        long startTicks,
        DateTimeOffset startTime,
        (string Id, string? Type) principal
    ) : IDisposable
    {
        public void Dispose() => listener.CompleteScope(context, startTicks, startTime, principal);
    }
}
