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
/// Partial containing the GraphQL field builders (query, mutation)
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

        var field = descriptor.Field(fieldName);

        if (HasTypedInput(registration))
            field.Argument("input", a => a.Type(NonNullInputType(registration.InputType)));

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
    /// Adds a single mutation field for a train. The field name is camelCase(trainName)
    /// with no run/queue prefix. Supports optional mode and priority arguments
    /// depending on the train's GraphQLOperations configuration.
    /// </summary>
    private static void AddMutationField(
        IObjectTypeDescriptor descriptor,
        TrainRegistration registration,
        string trainName
    )
    {
        var fieldName = char.ToLowerInvariant(trainName[0]) + trainName[1..];

        var field = descriptor.Field(fieldName);

        if (HasTypedInput(registration))
            field.Argument("input", a => a.Type(NonNullInputType(registration.InputType)));

        ApplyDescriptionAndDeprecation(field, registration);

        var hasRun = registration.GraphQLOperations.HasFlag(GraphQLOperation.Run);
        var hasQueue = registration.GraphQLOperations.HasFlag(GraphQLOperation.Queue);

        // Add mode argument when both Run and Queue are supported
        if (hasRun && hasQueue)
        {
            field.Argument(
                "mode",
                a =>
                    a.Type(new NamedTypeNode("ExecutionMode"))
                        .DefaultValue(new EnumValueNode("RUN"))
            );
        }

        // Add priority argument when Queue is supported
        if (hasQueue)
            field.Argument("priority", a => a.Type<IntType>());

        // Set return type to the per-train response type
        field.Type(new NamedTypeNode($"{trainName}Response"));

        // Resolver logic depends on the operations configuration
        if (hasRun && hasQueue)
        {
            field.Resolve(async ctx =>
            {
                var mode = ctx.ArgumentValue<string>("mode");
                if (mode == "QUEUE")
                    return await QueueTrainAsync(ctx, registration);
                return (object)await RunTrainAsync(ctx, registration);
            });
        }
        else if (hasQueue)
        {
            field.Resolve(async ctx => await QueueTrainAsync(ctx, registration));
        }
        else
        {
            field.Resolve(async ctx => await RunTrainAsync(ctx, registration));
        }
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
    /// Serializes the "input" argument from the resolver context and queues
    /// the train for async execution via ITrainExecutionService.
    /// </summary>
    private static async Task<QueueTrainResult> QueueTrainAsync(
        IResolverContext ctx,
        TrainRegistration registration
    )
    {
        var inputJson = SerializeInput(ctx, registration.InputType);
        var priority = ctx.ArgumentValue<int?>("priority") ?? 0;
        var executionService = ctx.Service<ITrainExecutionService>();
        return await executionService.QueueAsync(
            registration.ServiceTypeName,
            inputJson,
            priority,
            ctx.RequestAborted
        );
    }

    /// <summary>
    /// Extracts the "input" argument and serializes it to JSON using the
    /// Trax system serializer options. Returns "{}" for input types with no
    /// GraphQL-representable properties (empty records used for routing uniqueness).
    /// </summary>
    private static string SerializeInput(IResolverContext ctx, Type inputType)
    {
        if (!HasGraphQLRepresentableProperties(inputType))
            return "{}";

        var input = ctx.ArgumentValue<object>("input");
        return JsonSerializer.Serialize(
            input,
            inputType,
            TraxEffectConfiguration.StaticSystemJsonSerializerOptions
        );
    }

    /// <summary>
    /// Applies GraphQL description and deprecation reason to a field descriptor.
    /// </summary>
    private static void ApplyDescriptionAndDeprecation(
        IObjectFieldDescriptor field,
        TrainRegistration registration
    )
    {
        if (registration.GraphQLDescription is not null)
            field.Description(registration.GraphQLDescription);

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
