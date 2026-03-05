using System.Text.Json;
using HotChocolate.Language;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using Trax.Api.DTOs;
using Trax.Effect.Attributes;
using Trax.Effect.Configuration.TraxEffectConfiguration;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;

namespace Trax.Api.GraphQL.TypeModules;

/// <summary>
/// Partial containing the GraphQL field builders (query, run, queue)
/// and shared resolver helpers.
/// </summary>
public partial class TrainTypeModule
{
    /// <summary>
    /// Adds a query field that runs the train synchronously and returns
    /// either the typed output or a generic RunTrainResponse (metadataId only).
    /// </summary>
    private static void AddQueryField(
        IObjectTypeDescriptor descriptor,
        TrainRegistration registration,
        string trainName
    )
    {
        // Query fields use the derived name directly (no run/queue prefix)
        var fieldName = char.ToLowerInvariant(trainName[0]) + trainName[1..];

        var field = descriptor
            .Field(fieldName)
            .Argument("input", a => a.Type(NonNullInputType(registration.InputType)));

        ApplyDescriptionAndDeprecation(field, registration);

        if (HasTypedOutput(registration))
        {
            field
                .Type(NonNullObjectType(registration.OutputType))
                .Resolve(async ctx =>
                {
                    var result = await RunTrainAsync(ctx, registration);
                    return result.Output;
                });
        }
        else
        {
            field
                .Type<NonNullType<ObjectType<RunTrainResponse>>>()
                .Resolve(async ctx =>
                {
                    var result = await RunTrainAsync(ctx, registration);
                    return new RunTrainResponse(result.MetadataId);
                });
        }
    }

    /// <summary>
    /// Adds a "run{TrainName}" mutation field that executes the train synchronously.
    /// Returns a per-train response type (metadataId + output) for typed trains,
    /// or a generic RunTrainResponse for untyped trains.
    /// </summary>
    private static void AddRunField(
        IObjectTypeDescriptor descriptor,
        TrainRegistration registration,
        string trainName
    )
    {
        var field = descriptor
            .Field($"run{trainName}")
            .Argument("input", a => a.Type(NonNullInputType(registration.InputType)));

        ApplyDescriptionAndDeprecation(field, registration, prefix: "Run");

        if (HasTypedOutput(registration))
        {
            // Reference the eagerly-created Run{TrainName}Response type by name
            field
                .Type(new NamedTypeNode($"Run{trainName}Response"))
                .Resolve(async ctx => await RunTrainAsync(ctx, registration));
        }
        else
        {
            field
                .Type<NonNullType<ObjectType<RunTrainResponse>>>()
                .Resolve(async ctx =>
                {
                    var result = await RunTrainAsync(ctx, registration);
                    return new RunTrainResponse(result.MetadataId);
                });
        }
    }

    /// <summary>
    /// Adds a "queue{TrainName}" mutation field that enqueues the train for
    /// asynchronous execution. Always returns QueueTrainResponse (workQueueId + externalId).
    /// Accepts an optional priority argument (defaults to 0).
    /// </summary>
    private static void AddQueueField(
        IObjectTypeDescriptor descriptor,
        TrainRegistration registration,
        string trainName
    )
    {
        var field = descriptor
            .Field($"queue{trainName}")
            .Argument("input", a => a.Type(NonNullInputType(registration.InputType)))
            .Argument("priority", a => a.Type<IntType>());

        ApplyDescriptionAndDeprecation(field, registration, prefix: "Queue");

        field
            .Type<NonNullType<ObjectType<QueueTrainResponse>>>()
            .Resolve(async ctx =>
            {
                var inputJson = SerializeInput(ctx, registration.InputType);
                var priority = ctx.ArgumentValue<int?>("priority") ?? 0;
                var executionService = ctx.Service<ITrainExecutionService>();

                var result = await executionService.QueueAsync(
                    registration.ServiceTypeName,
                    inputJson,
                    priority,
                    ctx.RequestAborted
                );
                return new QueueTrainResponse(result.WorkQueueId, result.ExternalId);
            });
    }

    // ──────────────────────────────────────────────
    //  Shared helpers
    // ──────────────────────────────────────────────

    /// <summary>
    /// Serializes the "input" argument from the resolver context and executes
    /// the train synchronously via ITrainExecutionService.
    /// </summary>
    private static async Task<RunTrainResult> RunTrainAsync(
        IResolverContext ctx,
        TrainRegistration registration
    )
    {
        var inputJson = SerializeInput(ctx, registration.InputType);
        var executionService = ctx.Service<ITrainExecutionService>();
        return await executionService.RunAsync(
            registration.ServiceTypeName,
            inputJson,
            ctx.RequestAborted
        );
    }

    /// <summary>
    /// Extracts the "input" argument and serializes it to JSON using the
    /// Trax system serializer options.
    /// </summary>
    private static string SerializeInput(IResolverContext ctx, Type inputType)
    {
        var input = ctx.ArgumentValue<object>("input");
        return JsonSerializer.Serialize(
            input,
            inputType,
            TraxEffectConfiguration.StaticSystemJsonSerializerOptions
        );
    }

    /// <summary>
    /// Applies GraphQL description and deprecation reason to a field descriptor,
    /// optionally prefixing the description (e.g. "Run: ..." or "Queue: ...").
    /// </summary>
    private static void ApplyDescriptionAndDeprecation(
        IObjectFieldDescriptor field,
        TrainRegistration registration,
        string? prefix = null
    )
    {
        if (registration.GraphQLDescription is not null)
        {
            var description = prefix is null
                ? registration.GraphQLDescription
                : $"{prefix}: {registration.GraphQLDescription}";
            field.Description(description);
        }

        if (registration.GraphQLDeprecationReason is not null)
            field.Deprecated(registration.GraphQLDeprecationReason);
    }

    /// <summary>
    /// Builds a NonNullType&lt;InputObjectType&lt;T&gt;&gt; Type reference for the given CLR type.
    /// </summary>
    private static Type NonNullInputType(Type inputType) =>
        typeof(NonNullType<>).MakeGenericType(typeof(InputObjectType<>).MakeGenericType(inputType));

    /// <summary>
    /// Builds a NonNullType&lt;ObjectType&lt;T&gt;&gt; Type reference for the given CLR type.
    /// </summary>
    private static Type NonNullObjectType(Type outputType) =>
        typeof(NonNullType<>).MakeGenericType(typeof(ObjectType<>).MakeGenericType(outputType));
}
