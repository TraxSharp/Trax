using HotChocolate.Execution;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using HcRequestDelegate = HotChocolate.Execution.RequestDelegate;

namespace Trax.Api.GraphQL.Introspection;

/// <summary>
/// Request middleware that marks a request as allowed to use introspection when
/// <see cref="IntrospectionPolicy"/> says so. It sits in HotChocolate's execution pipeline just
/// before document validation, so every transport (HTTP POST, GET, multipart and WebSocket)
/// passes through it.
/// </summary>
/// <remarks>
/// Introspection is disabled for the schema at build time, which makes HotChocolate's
/// introspection rule refuse every request that does not carry an allowance. This middleware is
/// the only place Trax grants one, evaluated per request with that request's
/// <see cref="HttpContext"/>. An allowance the host granted itself (for example from its own
/// HTTP interceptor via <c>AllowIntrospection()</c> on the request builder) is left alone.
/// See <c>docs/adr/0012-introspection-is-decided-per-request.md</c>.
/// </remarks>
internal static class IntrospectionAllowanceMiddleware
{
    public const string Key = "Trax.IntrospectionAllowance";

    public static HcRequestDelegate Create(
        RequestMiddlewareFactoryContext factory,
        HcRequestDelegate next
    )
    {
        var policy = factory.Services.GetRequiredService<IntrospectionPolicy>();
        return context =>
        {
            if (policy.Allows(context.Features.Get<HttpContext>()))
            {
                var current = context.Features.Get<IntrospectionRequestOverrides>();
                context.Features.Set(
                    current is null
                        ? new IntrospectionRequestOverrides()
                        : current with
                        {
                            IsAllowed = true,
                        }
                );
            }

            return next(context);
        };
    }
}
