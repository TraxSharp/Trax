using System.Collections.Immutable;
using HotChocolate;
using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Trax.Api.GraphQL.PersistedOperations.Middleware;

/// <summary>
/// Enforces the persisted-operation policy inside HotChocolate's execution pipeline, right after
/// the document is parsed and before it is validated. Every transport reaches the executor
/// through this pipeline (HTTP POST, GET, multipart, WebSocket, and a request built in-process),
/// so each gets the same decision.
/// </summary>
/// <remarks>
/// <para>
/// A document the store supplied (<see cref="OperationDocumentInfo.IsPersisted"/>) always passes.
/// Anything else is inline, and <see cref="PersistedOperationPolicy"/> decides it from the
/// operation name, the document id and the parsed document. A refusal is the
/// <c>PERSISTED_OPERATION_REQUIRED</c> error with HTTP status 400.
/// </para>
/// <para>
/// Host code that builds a request itself can exempt it with HotChocolate's own
/// <c>AllowNonPersistedOperation()</c> on the request builder. No Trax transport sets it.
/// </para>
/// <para>See <c>docs/adr/0013-persisted-operation-enforcement-runs-in-the-execution-pipeline.md</c>.</para>
/// </remarks>
internal static class PersistedOperationEnforcementMiddleware
{
    public const string Key = "Trax.PersistedOperationEnforcement";

    internal const string ErrorCode = "PERSISTED_OPERATION_REQUIRED";

    internal const string ErrorMessage = "Only persisted operations are accepted on this server.";

    public static RequestDelegate Create(
        RequestMiddlewareFactoryContext factory,
        RequestDelegate next
    )
    {
        var policy = factory.Services.GetRequiredService<PersistedOperationPolicy>();
        var logger = factory
            .Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(PersistedOperationEnforcementMiddleware).FullName!);

        return context =>
        {
            if (context.IsWarmupRequest())
                return next(context);

            var info = context.OperationDocumentInfo;
            if (info.IsPersisted)
                return next(context);

            if (
                context
                    .Features.Get<PersistedOperationRequestOverrides>()
                    ?.AllowNonPersistedOperation == true
            )
                return next(context);

            var operationName = context.Request.OperationName;
            var documentId = context.Request.DocumentId.IsEmpty
                ? null
                : context.Request.DocumentId.Value;

            switch (policy.Decide(operationName, documentId, info.Document))
            {
                case PersistedOperationDecision.Reject:
                    if (policy.Options.LogNonPersistedRequests)
                        logger.LogInformation(
                            "Trax persisted-operations rejecting inline query (operationName={OperationName})",
                            operationName
                        );
                    context.Result = Refusal();
                    return default;

                case PersistedOperationDecision.Log when policy.Options.LogNonPersistedRequests:
                    logger.LogInformation(
                        "Trax persisted-operations observed inline query (operationName={OperationName}, willReject={WillReject})",
                        operationName,
                        policy.Options.RequirePersisted
                    );
                    break;
            }

            return next(context);
        };
    }

    private static OperationResult Refusal() =>
        new(
            ImmutableList.Create(
                ErrorBuilder.New().SetMessage(ErrorMessage).SetCode(ErrorCode).Build()
            ),
            null
        )
        {
            ContextData = ImmutableDictionary<string, object?>.Empty.Add(
                ExecutionContextData.HttpStatusCode,
                400
            ),
        };
}
