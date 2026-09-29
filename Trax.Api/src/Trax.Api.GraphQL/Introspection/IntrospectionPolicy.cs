using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace Trax.Api.GraphQL.Introspection;

/// <summary>
/// Decides, for one request, whether the Trax schema may be read: introspection fields in an
/// operation, the schema download (<c>?sdl</c>, <c>/schema</c>, <c>/schema.graphql</c>) and the
/// GraphQL IDE all follow this one answer.
/// </summary>
/// <remarks>
/// <para>
/// A host predicate given to <c>AllowIntrospection</c> decides whenever it is set, in every
/// environment. Without one, Development allows and every other environment refuses.
/// </para>
/// <para>
/// The predicate needs the request, so a request that carries none is answered by the
/// environment alone: HotChocolate's HTTP and WebSocket transports both attach the
/// <see cref="HttpContext"/>, so a request without one was built in-process by host code.
/// </para>
/// <para>See <c>docs/adr/0012-introspection-is-decided-per-request.md</c>.</para>
/// </remarks>
internal sealed class IntrospectionPolicy(
    Predicate<HttpContext>? predicate,
    IHostEnvironment? environment
)
{
    private readonly bool _isDevelopment = environment?.IsDevelopment() == true;

    public bool Allows(HttpContext? httpContext)
    {
        if (httpContext is null || predicate is null)
            return _isDevelopment;

        return predicate(httpContext);
    }
}
