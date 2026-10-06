using HotChocolate;
using HotChocolate.Execution;

namespace Trax.Api.GraphQL.Subscriptions;

/// <summary>
/// Refuses an operation that <see cref="TraxCompositeSocketInterceptor"/> marked because its
/// connection already runs as many operations as it may, before the document is looked up.
/// </summary>
/// <remarks>
/// The interceptor cannot refuse the operation itself: HotChocolate masks an exception thrown
/// while it builds the request. The mark travels in the request's global state and is turned into
/// a <c>TRAX_SOCKET_OPERATION_LIMIT</c> error here.
/// See <c>docs/adr/0015-a-socket-runs-a-bounded-number-of-operations.md</c>.
/// </remarks>
internal static class SocketOperationLimitRequestMiddleware
{
    /// <summary>The pipeline key, for ordering relative to HotChocolate's own middleware.</summary>
    public const string Key = "Trax.SocketOperationLimit";

    /// <summary>The global-state key that marks an operation past its connection's limit.</summary>
    public const string ExceededKey = "Trax.SocketOperationLimitExceeded";

    /// <summary>The error code an operation past the limit gets.</summary>
    public const string ErrorCode = "TRAX_SOCKET_OPERATION_LIMIT";

    public static RequestDelegate Create(RequestDelegate next) =>
        context =>
        {
            if (context.ContextData.ContainsKey(ExceededKey))
                throw new GraphQLException(
                    ErrorBuilder
                        .New()
                        .SetMessage(
                            "This connection already runs as many operations as it may. "
                                + "Complete one before starting another."
                        )
                        .SetCode(ErrorCode)
                        .Build()
                );

            return next(context);
        };
}
