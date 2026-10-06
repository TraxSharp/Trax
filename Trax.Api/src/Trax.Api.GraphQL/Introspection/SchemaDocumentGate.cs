using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Trax.Api.GraphQL.Introspection;

/// <summary>
/// Puts the schema download and the GraphQL IDE on the Trax endpoint behind
/// <see cref="IntrospectionPolicy"/>. HotChocolate serves both from the same endpoint as
/// execution, and its switches for them are fixed when the endpoint is built, so the decision is
/// taken here per request by wrapping the endpoint's request delegate.
/// </summary>
/// <remarks>
/// <para>
/// A GET or HEAD that is not a WebSocket upgrade is a schema or IDE request when it asks for the
/// schema (<c>?sdl</c>, <c>/schema</c>, <c>/schema/</c>, <c>/schema.graphql</c>), asks for HTML,
/// or carries no operation at all (no <c>query</c>, <c>id</c> or <c>extensions</c> parameter).
/// Everything else goes straight to HotChocolate; an operation that reads the schema is judged
/// in the execution pipeline by <see cref="IntrospectionAllowanceMiddleware"/>.
/// </para>
/// <para>
/// A refused request gets 404, the same answer HotChocolate gives when those features are
/// switched off. An allowed schema download is marked <c>private</c>: it was decided for this
/// caller, so a shared cache must not hand it to the next one.
/// </para>
/// <para>See <c>docs/adr/0012-introspection-is-decided-per-request.md</c>.</para>
/// </remarks>
internal static class SchemaDocumentGate
{
    public static void Apply(
        IEndpointConventionBuilder endpoint,
        PathString routePrefix,
        IntrospectionPolicy policy
    ) =>
        endpoint.Add(builder =>
        {
            var inner = builder.RequestDelegate;
            if (inner is null)
                return;

            builder.RequestDelegate = context =>
            {
                if (!IsSchemaOrToolRequest(context.Request, routePrefix, out var isSchemaDownload))
                    return inner(context);

                if (!policy.Allows(context))
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return Task.CompletedTask;
                }

                if (isSchemaDownload)
                    context.Response.OnStarting(() =>
                    {
                        context.Response.Headers.CacheControl = "private, no-cache";
                        return Task.CompletedTask;
                    });

                return inner(context);
            };
        });

    internal static bool IsSchemaOrToolRequest(
        HttpRequest request,
        PathString routePrefix,
        out bool isSchemaDownload
    )
    {
        isSchemaDownload = false;

        if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method))
            return false;

        if (request.HttpContext.WebSockets.IsWebSocketRequest)
            return false;

        isSchemaDownload = request.Query.ContainsKey("sdl") || IsSchemaPath(request, routePrefix);
        if (isSchemaDownload)
            return true;

        if (AcceptsHtml(request))
            return true;

        var query = request.Query;
        return !query.ContainsKey("query")
            && !query.ContainsKey("id")
            && !query.ContainsKey("extensions");
    }

    private static bool IsSchemaPath(HttpRequest request, PathString routePrefix)
    {
        if (
            !request.Path.StartsWithSegments(
                routePrefix,
                StringComparison.OrdinalIgnoreCase,
                out var rest
            )
        )
            return false;

        return rest.Equals("/schema", StringComparison.OrdinalIgnoreCase)
            || rest.Equals("/schema/", StringComparison.OrdinalIgnoreCase)
            || rest.Equals("/schema.graphql", StringComparison.OrdinalIgnoreCase);
    }

    private static bool AcceptsHtml(HttpRequest request)
    {
        foreach (var value in request.Headers.Accept)
            if (
                value is not null
                && value.Contains("text/html", StringComparison.OrdinalIgnoreCase)
            )
                return true;
        return false;
    }
}
