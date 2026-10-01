using System.Reflection;
using HotChocolate;
using HotChocolate.Data.Filters;
using HotChocolate.Data.Sorting;
using HotChocolate.Execution;
using HotChocolate.Types;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.Auth;
using Trax.Api.GraphQL.Configuration;

namespace Trax.Api.GraphQL.Startup;

/// <summary>
/// Fails the host at startup when a <c>[TraxQueryModel]</c> reaches an entity that declares no
/// authorization posture, through its object type or through its filter or sort input.
/// </summary>
/// <remarks>
/// <para>
/// HotChocolate infers an object type, a filter input and a sort input for every navigation a
/// query model exposes, binding every public property of the target. The target is then as
/// reachable as the model, so it answers the question <see cref="ExposureAuthorizationRule"/>
/// asks of the model: <c>[TraxAuthorize]</c> to gate it, <c>[TraxAllowAnonymous]</c> to open it,
/// or an endpoint gated with <c>RequireAuthorization()</c>. See
/// docs/adr/0025-every-entity-a-query-model-reaches-declares-its-posture.md.
/// </para>
/// <para>
/// The check reads the built schema rather than the CLR types, so a navigation the model's
/// exposed field set leaves out (<c>BindFields = Explicit</c>, <c>ExposeAs</c>) needs no
/// declaration, and it asks the model's <c>DbContext</c> which classes are entities, so an owned
/// type is treated as part of the entity that owns it and walked through rather than reported.
/// </para>
/// </remarks>
internal sealed class QueryModelReachValidator(
    GraphQLConfiguration configuration,
    IServiceProvider serviceProvider
) : StartupGate
{
    /// <summary>Schema name registered by Trax for the GraphQL endpoint.</summary>
    private const string TraxSchemaName = "trax";

    protected override async Task CheckAsync(CancellationToken cancellationToken)
    {
        using var scope = serviceProvider.CreateScope();
        var executor = await scope
            .ServiceProvider.GetRequiredService<IRequestExecutorProvider>()
            .GetExecutorAsync(TraxSchemaName, cancellationToken);
        var schema = executor.Schema;

        var models = configuration.ModelRegistrations.Select(r => r.EntityType).ToHashSet();
        var entities = EntityTypes(scope.ServiceProvider);

        // Entity -> the schema coordinates through which a query model reaches it.
        var reached = new Dictionary<Type, SortedSet<string>>();

        void Reach(Type entity, string coordinate)
        {
            if (!reached.TryGetValue(entity, out var coordinates))
                reached[entity] = coordinates = new SortedSet<string>(StringComparer.Ordinal);
            coordinates.Add(coordinate);
        }

        WalkObjectTypes(schema, models, entities, Reach);
        WalkInputTypes(schema, models, entities, Reach);

        var messages = new List<string>();
        foreach (var (entity, coordinates) in reached.OrderBy(r => r.Key.FullName))
        {
            var posture = NavigationTargetPosture.Read(entity);
            var violation = ExposureAuthorizationRule.Evaluate(
                hasAuthorize: posture.AuthorizeAttributes.Count > 0,
                hasAllowAnonymous: posture.AllowAnonymous,
                endpointGated: configuration.AuthorizationRequired
            );

            if (violation is not ExposureViolation.None)
            {
                messages.Add(BuildMessage(entity, coordinates, violation));
                continue;
            }

            if (posture.IsGated)
                VerifyGateEmitted(schema, entity, messages);
        }

        if (messages.Count == 0)
            return;

        throw new InvalidOperationException(
            $"{messages.Count} entit{(messages.Count == 1 ? "y" : "ies")} reached from a "
                + "[TraxQueryModel] declare no usable authorization posture (see "
                + "docs/adr/0025-every-entity-a-query-model-reaches-declares-its-posture.md):"
                + Environment.NewLine
                + Environment.NewLine
                + string.Join(Environment.NewLine + Environment.NewLine, messages)
        );
    }

    /// <summary>
    /// Follows the navigations on every query model's object type: the fields HotChocolate bound
    /// from a property of the entity itself, which is what a navigation is. A field a type
    /// extension contributes declares its own posture under api/0003 and is not followed.
    /// </summary>
    private static void WalkObjectTypes(
        ISchemaDefinition schema,
        HashSet<Type> models,
        Func<Type, bool> isEntity,
        Action<Type, string> reach
    )
    {
        var objectTypes = schema.Types.OfType<ObjectType>().ToList();
        var queue = new Queue<ObjectType>(objectTypes.Where(t => models.Contains(t.RuntimeType)));
        var visited = new HashSet<ObjectType>(queue);

        while (queue.TryDequeue(out var type))
        {
            foreach (var field in type.Fields)
            {
                if (field.IsIntrospectionField)
                    continue;

                if (
                    (field.ResolverMember ?? field.Member) is not PropertyInfo property
                    || property.DeclaringType?.IsAssignableFrom(type.RuntimeType) != true
                )
                    continue;

                if (field.Type.NamedType() is not ObjectType target)
                    continue;

                var runtime = target.RuntimeType;
                if (models.Contains(runtime))
                    continue;

                if (isEntity(runtime))
                    reach(runtime, $"{type.Name}.{field.Name}");

                // An owned value is part of the entity that holds it, and an entity's own
                // navigations are as reachable as it is: both are walked.
                if (visited.Add(target))
                    queue.Enqueue(target);
            }
        }
    }

    /// <summary>
    /// Every filter and sort input in the schema descends from a query model's <c>where</c> or
    /// <c>order</c> argument, so a field of one whose input is built over an entity is a
    /// navigation the caller can filter or sort through.
    /// </summary>
    private static void WalkInputTypes(
        ISchemaDefinition schema,
        HashSet<Type> models,
        Func<Type, bool> isEntity,
        Action<Type, string> reach
    )
    {
        foreach (var input in schema.Types.OfType<InputObjectType>())
        {
            if (input is not (IFilterInputType or ISortInputType))
                continue;

            foreach (var field in input.Fields)
            {
                if (EntityOf(field.Type.NamedType()) is not { } runtime)
                    continue;

                if (models.Contains(runtime) || !isEntity(runtime))
                    continue;

                reach(runtime, $"{input.Name}.{field.Name}");
            }
        }
    }

    /// <summary>
    /// The runtime type an input is built over, looking through HotChocolate's list filter
    /// (<c>some</c>/<c>all</c>/<c>none</c>) to the element input.
    /// </summary>
    private static Type? EntityOf(ITypeDefinition type)
    {
        switch (type)
        {
            case IFilterInputType filter
                when filter.GetType().IsGenericType
                    && filter.GetType().GetGenericTypeDefinition() == typeof(ListFilterInputType<>):
                return
                    filter.Fields.FirstOrDefault(f => f.Name == "some")?.Type.NamedType()
                        is IFilterInputType element
                    ? element.EntityType.Source
                    : null;
            case IFilterInputType filter:
                return filter.EntityType.Source;
            case ISortInputType sort:
                return sort.EntityType.Source;
            default:
                return null;
        }
    }

    /// <summary>
    /// The entity classes of every <c>DbContext</c> the query models come from. A class EF Core
    /// maps as owned, or as a property bag, is not an entity of its own.
    /// </summary>
    /// <remarks>
    /// When a context cannot be resolved, every class reached is treated as an entity, so the
    /// check fails closed rather than passing a navigation it could not classify.
    /// </remarks>
    private Func<Type, bool> EntityTypes(IServiceProvider services)
    {
        var entities = new HashSet<Type>();

        foreach (
            var contextType in configuration
                .ModelRegistrations.Select(r => r.DbContextType)
                .Distinct()
        )
        {
            if (services.GetService(contextType) is not DbContext context)
                return type => type.IsClass && type != typeof(object);

            foreach (var entityType in context.Model.GetEntityTypes())
            {
                if (entityType.IsOwned() || entityType.HasSharedClrType)
                    continue;
                entities.Add(entityType.ClrType);
            }
        }

        return entities.Contains;
    }

    /// <summary>
    /// A gated navigation target's object type must carry the <c>@authorize</c> Trax emitted for
    /// it, the same invariant <see cref="QueryModelAuthorizationSchemaValidator"/> holds for a
    /// gated model.
    /// </summary>
    private static void VerifyGateEmitted(
        ISchemaDefinition schema,
        Type entity,
        List<string> messages
    )
    {
        var objectType = schema
            .Types.OfType<ObjectType>()
            .FirstOrDefault(t => t.RuntimeType == entity);

        if (objectType is null || objectType.Directives.ContainsDirective("authorize"))
            return;

        messages.Add(
            $"Entity '{entity.FullName}' declares [TraxAuthorize], but its object type "
                + $"'{objectType.Name}' carries no @authorize directive in the built schema. A "
                + "ConfigureSchema callback has replaced the type with an ungated one. Remove the "
                + "override, or remove [TraxAuthorize] from the entity if the exposure is intended."
        );
    }

    private static string BuildMessage(
        Type entity,
        IEnumerable<string> coordinates,
        ExposureViolation violation
    )
    {
        var through = string.Join(", ", coordinates.Select(c => $"'{c}'"));

        return violation switch
        {
            ExposureViolation.MissingMarker =>
                $"Entity '{entity.FullName}' is not a [TraxQueryModel], but a query model reaches "
                    + $"it through {through}, which exposes its columns to reading, filtering or "
                    + "sorting. It declares neither [TraxAuthorize] nor [TraxAllowAnonymous]. Every "
                    + "entity a query model reaches must state its authorization posture: add "
                    + "[TraxAuthorize] (optionally with a policy or roles) to the entity class to gate "
                    + "it, or [TraxAllowAnonymous] to open it to anonymous callers. To keep it out of "
                    + "the schema instead, leave the navigation out of the model's exposed field set "
                    + "(BindFields = Explicit or ExposeAs). To gate the entire endpoint, call "
                    + "AddTraxGraphQL(graphql => graphql.RequireAuthorization(...)).",
            _ => ExposureAuthorizationRule.BuildMessage(
                $"Entity reached from a query model through {through},",
                entity.FullName!,
                violation
            ),
        };
    }
}
