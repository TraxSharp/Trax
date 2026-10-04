using System.Runtime.CompilerServices;
using HotChocolate;
using HotChocolate.Authorization;
using HotChocolate.Data.Filters;
using HotChocolate.Data.Sorting;
using HotChocolate.Language;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using HotChocolate.Types.Descriptors.Configurations;
using Microsoft.Extensions.DependencyInjection;

namespace Trax.Api.GraphQL.Authorization;

/// <summary>
/// Carries a gated type's authorization into the filter and sort inputs that reach it, so a
/// caller filters or sorts through a navigation only when they could read the type at the end
/// of it.
/// </summary>
/// <remarks>
/// <para>
/// HotChocolate's <c>@authorize</c> applies to object types and fields only; an input field
/// cannot carry it. A type-level gate therefore fires when a node of the type is resolved, and
/// never when a <c>where</c> or <c>order</c> argument reaches the type through a navigation,
/// although a predicate over a column reveals the column's value a comparison at a time.
/// </para>
/// <para>
/// This middleware runs on every field that takes a filter or sort argument, ahead of the
/// filtering and sorting middleware: a query model's entry field, a type extension's resolver,
/// a filtered navigation on an entity. <see cref="NavigationInputAuthorizationInterceptor"/>
/// attaches it to each one, so the rule does not depend on who contributed the field. It walks
/// the arguments the caller supplied against their input types, collects every input built over a
/// type whose object type carries <c>@authorize</c>, and evaluates those directives through
/// HotChocolate's own <see cref="IAuthorizationHandler"/>, the same call the <c>@authorize</c>
/// middleware makes. A refusal produces the error that middleware produces. Inputs the caller did
/// not use cost nothing, so reading a gated type stays a matter of selecting it, as it was.
/// See <c>docs/adr/0025-every-entity-a-query-model-reaches-declares-its-posture.md</c>.
/// </para>
/// </remarks>
internal static class NavigationInputAuthorization
{
    /// <summary>Per schema: runtime type → the @authorize directives on its object type.</summary>
    private static readonly ConditionalWeakTable<
        ISchemaDefinition,
        Dictionary<Type, AuthorizeDirective[]>
    > GatesBySchema = new();

    /// <summary>
    /// The key the middleware is registered under, so a field never carries it twice.
    /// </summary>
    public const string MiddlewareKey = "Trax.NavigationInputAuthorization";

    /// <summary>
    /// Attaches the middleware to the entry field of <paramref name="entityType"/>. The entity's
    /// own gate is the entry field's directive, so it is not evaluated twice.
    /// </summary>
    public static void Apply(IObjectFieldDescriptor field, Type entityType) =>
        field
            .Extend()
            .Configuration.MiddlewareConfigurations.Add(
                new FieldMiddlewareConfiguration(Create(entityType), key: MiddlewareKey)
            );

    /// <summary>
    /// Builds the middleware. <paramref name="coveredEntity"/> is the type whose gate the field
    /// already carries as its own directive, if any; every other type the inputs reach is
    /// evaluated.
    /// </summary>
    public static FieldMiddleware Create(Type? coveredEntity) =>
        next =>
            async context =>
            {
                var gates = GatesBySchema.GetValue(context.Schema, BuildGates);
                if (gates.Count == 0)
                {
                    await next(context).ConfigureAwait(false);
                    return;
                }

                var reached = new HashSet<Type>();
                foreach (var argument in context.Selection.Field.Arguments)
                {
                    if (argument.Type.NamedType() is not (IFilterInputType or ISortInputType))
                        continue;

                    Walk(
                        context.ArgumentLiteral<IValueNode>(argument.Name),
                        argument.Type.NamedType(),
                        reached
                    );
                }

                if (coveredEntity is not null)
                    reached.Remove(coveredEntity);

                foreach (var type in reached)
                {
                    if (!gates.TryGetValue(type, out var directives))
                        continue;

                    foreach (var directive in directives)
                    {
                        var result = await AuthorizeAsync(context, directive).ConfigureAwait(false);
                        if (result is AuthorizeResult.Allowed)
                            continue;

                        context.ReportError(BuildError(context, directive, result));
                        context.Result = null;
                        return;
                    }
                }

                await next(context).ConfigureAwait(false);
            };

    /// <summary>
    /// Evaluates the directive through HotChocolate's handler, which every Trax schema registers.
    /// Were it missing, resolving it throws and the field fails, so the gate still refuses.
    /// </summary>
    private static ValueTask<AuthorizeResult> AuthorizeAsync(
        IMiddlewareContext context,
        AuthorizeDirective directive
    ) =>
        context
            .Services.GetRequiredService<IAuthorizationHandler>()
            .AuthorizeAsync(context, directive, context.RequestAborted);

    /// <summary>
    /// Records the runtime type of every filter or sort input the value uses, descending through
    /// nested objects (navigations, <c>and</c>/<c>or</c>, <c>some</c>/<c>all</c>) and lists.
    /// </summary>
    private static void Walk(IValueNode value, ITypeDefinition type, HashSet<Type> reached)
    {
        switch (value)
        {
            case ListValueNode list:
                foreach (var item in list.Items)
                    Walk(item, type, reached);
                return;

            case ObjectValueNode obj when type is InputObjectType input:
                switch (input)
                {
                    case IFilterInputType filter:
                        reached.Add(filter.EntityType.Source);
                        break;
                    case ISortInputType sort:
                        reached.Add(sort.EntityType.Source);
                        break;
                }

                foreach (var fieldNode in obj.Fields)
                {
                    if (input.Fields.TryGetField(fieldNode.Name.Value, out var field))
                        Walk(fieldNode.Value, field.Type.NamedType(), reached);
                }
                return;
        }
    }

    private static Dictionary<Type, AuthorizeDirective[]> BuildGates(ISchemaDefinition schema)
    {
        var gates = new Dictionary<Type, AuthorizeDirective[]>();

        foreach (var objectType in schema.Types.OfType<ObjectType>())
        {
            if (objectType.RuntimeType == typeof(object))
                continue;

            var directives = objectType
                .Directives.Where(d => d.Type.Name == "authorize")
                .Select(d => d.ToValue<AuthorizeDirective>())
                .ToArray();

            if (directives.Length > 0)
                gates[objectType.RuntimeType] = directives;
        }

        return gates;
    }

    /// <summary>The error HotChocolate's <c>@authorize</c> middleware reports for the same outcome.</summary>
    private static IError BuildError(
        IMiddlewareContext context,
        AuthorizeDirective directive,
        AuthorizeResult result
    ) =>
        result switch
        {
            AuthorizeResult.NoDefaultPolicy => ErrorBuilder
                .New()
                .SetMessage("The default authorization policy does not exist.")
                .SetCode(ErrorCodes.Authentication.NoDefaultPolicy)
                .SetPath(context.Path)
                .Build(),
            AuthorizeResult.PolicyNotFound => ErrorBuilder
                .New()
                .SetMessage($"The `{directive.Policy}` authorization policy does not exist.")
                .SetCode(ErrorCodes.Authentication.PolicyNotFound)
                .SetPath(context.Path)
                .Build(),
            _ => ErrorBuilder
                .New()
                .SetMessage("The current user is not authorized to access this resource.")
                .SetCode(
                    result == AuthorizeResult.NotAllowed
                        ? ErrorCodes.Authentication.NotAuthorized
                        : ErrorCodes.Authentication.NotAuthenticated
                )
                .SetPath(context.Path)
                .Build(),
        };
}
