using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Trax.Api.GraphQL.PersistedOperations.Configuration;

namespace Trax.Api.GraphQL.PersistedOperations.Middleware;

/// <summary>
/// ASP.NET middleware that enforces persisted-operations policy on inbound
/// GraphQL POST requests. Rejects, logs, or passes through based on
/// <see cref="PersistedOperationsOptions"/>. Handles batched requests
/// (JSON-array bodies) by enforcing the policy on every entry.
/// </summary>
/// <remarks>
/// Register via <c>app.UsePersistedOperationsEnforcement()</c> AFTER
/// <c>UseRouting()</c> and BEFORE <c>UseTraxGraphQL()</c>. The middleware
/// reads the request body once with buffering enabled so HotChocolate's
/// downstream parser can re-read it.
/// </remarks>
internal sealed class PersistedOperationsMiddleware
{
    private readonly RequestDelegate _next;
    private readonly PersistedOperationsOptions _options;
    private readonly AllowlistMatcher _allowlist;
    private readonly ILogger<PersistedOperationsMiddleware> _logger;

    public PersistedOperationsMiddleware(
        RequestDelegate next,
        PersistedOperationsOptions options,
        AllowlistMatcher allowlist,
        ILogger<PersistedOperationsMiddleware> logger
    )
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(allowlist);
        ArgumentNullException.ThrowIfNull(logger);
        _next = next;
        _options = options;
        _allowlist = allowlist;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!ShouldInspect(context))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        context.Request.EnableBuffering();
        var parsedAll = await ParseGraphQLBodyAsync(context.Request.Body, context.RequestAborted)
            .ConfigureAwait(false);
        context.Request.Body.Position = 0;

        if (parsedAll is null || parsedAll.Count == 0)
        {
            // Body was unreadable or empty. Let HC handle the error.
            await _next(context).ConfigureAwait(false);
            return;
        }

        // Batched requests: enforce on every entry. Any rejected entry
        // rejects the whole batch (consistent with single-entry behavior).
        foreach (var parsed in parsedAll)
        {
            switch (Decide(parsed))
            {
                case Decision.Reject:
                    if (_options.LogNonPersistedRequests)
                    {
                        _logger.LogInformation(
                            "Trax persisted-operations rejecting inline query (operationName={OperationName})",
                            parsed.OperationName
                        );
                    }
                    await WritePersistedRequiredErrorAsync(context).ConfigureAwait(false);
                    return;
                case Decision.Log when _options.LogNonPersistedRequests:
                    _logger.LogInformation(
                        "Trax persisted-operations observed inline query (operationName={OperationName}, willReject={WillReject})",
                        parsed.OperationName,
                        _options.RequirePersisted
                    );
                    break;
            }
        }

        await _next(context).ConfigureAwait(false);
    }

    private enum Decision
    {
        PassThrough,
        Log,
        Reject,
    }

    private Decision Decide(GraphQLRequestShape parsed)
    {
        var inlineQueryPresent = !string.IsNullOrWhiteSpace(parsed.Query);
        if (!inlineQueryPresent)
            return Decision.PassThrough;

        if (_allowlist.IsAllowed(parsed.OperationName, parsed.DocumentId))
            return Decision.PassThrough;

        if (_options.AllowIntrospection && IsIntrospection(parsed))
            return Decision.PassThrough;

        if (!_options.RequirePersisted)
            return Decision.Log;

        return Decision.Reject;
    }

    private static bool ShouldInspect(HttpContext context)
    {
        if (!HttpMethods.IsPost(context.Request.Method))
            return false;

        var contentType = context.Request.ContentType;
        if (string.IsNullOrEmpty(contentType))
            return false;

        // Accept the family of GraphQL JSON content types. HC v15 emits
        // application/graphql-response+json on responses; some clients send
        // it on requests too. application/graphql is the legacy media type.
        return contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase)
            || contentType.StartsWith("application/graphql", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsIntrospection(GraphQLRequestShape req) =>
        IntrospectionDetector.LooksLikeIntrospectionByName(req.OperationName)
        || (req.Query is not null && IntrospectionDetector.IsPureIntrospection(req.Query));

    /// <summary>
    /// Parses a GraphQL HTTP body. Returns a list of one entry for a
    /// single-operation request, or N entries for a batched request
    /// (JSON-array root). Returns null on malformed bodies.
    /// </summary>
    private static async Task<IReadOnlyList<GraphQLRequestShape>?> ParseGraphQLBodyAsync(
        Stream body,
        CancellationToken ct
    )
    {
        try
        {
            using var doc = await JsonDocument
                .ParseAsync(body, cancellationToken: ct)
                .ConfigureAwait(false);

            return doc.RootElement.ValueKind switch
            {
                JsonValueKind.Object => new[] { ReadEntry(doc.RootElement) },
                JsonValueKind.Array => doc
                    .RootElement.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.Object)
                    .Select(ReadEntry)
                    .ToArray(),
                _ => null,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static GraphQLRequestShape ReadEntry(JsonElement element)
    {
        string? query = null;
        string? documentId = null;
        string? operationName = null;

        if (element.TryGetProperty("query", out var q) && q.ValueKind == JsonValueKind.String)
            query = q.GetString();

        if (
            element.TryGetProperty("documentId", out var dId)
            && dId.ValueKind == JsonValueKind.String
        )
            documentId = dId.GetString();
        else if (element.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
            documentId = id.GetString();

        if (
            element.TryGetProperty("operationName", out var op)
            && op.ValueKind == JsonValueKind.String
        )
            operationName = op.GetString();

        return new GraphQLRequestShape(query, documentId, operationName);
    }

    private static async Task WritePersistedRequiredErrorAsync(HttpContext context)
    {
        const string body =
            "{\"errors\":[{\"message\":\"Only persisted operations are accepted on this server.\","
            + "\"extensions\":{\"code\":\"PERSISTED_OPERATION_REQUIRED\"}}]}";

        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        context.Response.ContentType = "application/json; charset=utf-8";
        await context
            .Response.Body.WriteAsync(Encoding.UTF8.GetBytes(body), context.RequestAborted)
            .ConfigureAwait(false);
    }

    internal readonly record struct GraphQLRequestShape(
        string? Query,
        string? DocumentId,
        string? OperationName
    );
}
