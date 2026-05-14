using HotChocolate.Authorization;
using HotChocolate.Execution;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Api.GraphQL.Configuration;
using Trax.Api.GraphQL.TypeModules;
using Trax.Effect.Attributes;

namespace Trax.Api.GraphQL.Startup;

/// <summary>
/// Post-build invariant check that every <c>[TraxAuthorize]</c>-decorated
/// <c>[TraxQueryModel]</c> entity still carries the <c>@authorize</c>
/// directive on its <c>ObjectType</c> and on its entry field under
/// <c>discover</c> after the schema is fully built.
///
/// <para>
/// This closes the only remaining escape hatch: a consumer-supplied
/// <c>ConfigureSchema</c> callback (which runs after the standard Trax
/// type-module wiring) could in principle add or replace types in a way that
/// strips the directives Trax attached. The discovery, registration, and
/// type-module layers all run inside Trax-controlled code and have no
/// "skip auth" knob, but <c>ConfigureSchema</c> has full <c>IRequestExecutorBuilder</c>
/// access by design. This validator runs at host start, materialises the
/// schema, and re-asserts the invariant. If a gate has been removed, the
/// host fails to start with a message naming the entity and the missing
/// directive location.
/// </para>
///
/// <para>
/// The check is read-only and idempotent. It does not modify the schema.
/// It runs once at startup and never again.
/// </para>
/// </summary>
internal sealed class QueryModelAuthorizationSchemaValidator(
    GraphQLConfiguration configuration,
    IServiceProvider serviceProvider
) : IHostedService
{
    /// <summary>Schema name registered by Trax for the GraphQL endpoint.</summary>
    private const string TraxSchemaName = "trax";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var gated = configuration
            .ModelRegistrations.Where(r => r.AuthorizeAttributes.Count > 0)
            .ToList();

        if (gated.Count == 0)
            return;

        // Materialise the schema. Building it now (a) catches any other schema
        // misconfiguration at host start instead of on the first request, and
        // (b) lets us walk the resolved types.
        using var scope = serviceProvider.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IRequestExecutorResolver>();
        var executor = await resolver.GetRequestExecutorAsync(TraxSchemaName, cancellationToken);
        var schema = executor.Schema;

        var queryType = schema.QueryType;

        foreach (var reg in gated)
        {
            VerifyTypeLevelDirective(schema, reg);
            VerifyEntryFieldDirective(queryType, reg);
        }
    }

    /// <summary>
    /// Walks the schema for the <see cref="ObjectType"/> built from the entity's CLR
    /// type and confirms it has at least one <c>@authorize</c> directive. This is the
    /// gate that enforces transitive navigation — without it, any field elsewhere in
    /// the schema whose return type is this entity would be readable.
    /// </summary>
    private static void VerifyTypeLevelDirective(ISchema schema, QueryModelRegistration reg)
    {
        var objectType = schema
            .Types.OfType<ObjectType>()
            .FirstOrDefault(t => t.RuntimeType == reg.EntityType);

        if (objectType is null)
            throw new InvalidOperationException(
                $"[TraxAuthorize] invariant violated: no ObjectType for "
                    + $"'{reg.EntityType.FullName}' is present in the built schema. "
                    + "A ConfigureSchema callback may have replaced or removed it. "
                    + "Trax cannot enforce type-level authorization on a type it "
                    + "cannot find."
            );

        if (!HasAuthorizeDirective(objectType.Directives))
            throw new InvalidOperationException(
                $"[TraxAuthorize] invariant violated: ObjectType for "
                    + $"'{reg.EntityType.FullName}' has no @authorize directive in "
                    + "the built schema. The directive was attached during type-module "
                    + "wiring but is now missing — a ConfigureSchema callback has "
                    + "replaced the ObjectType with an unauthorized variant. Remove the "
                    + "override, or remove [TraxAuthorize] from the entity if the "
                    + "exposure is intentional."
            );
    }

    /// <summary>
    /// Walks the <c>discover</c> namespace down to the entry field for this entity
    /// and confirms the field carries an <c>@authorize</c> directive. The field-level
    /// gate is what blocks Connection-shaped scalars (<c>totalCount</c>, <c>pageInfo</c>)
    /// from leaking through when the request never resolves an entity node.
    /// </summary>
    private static void VerifyEntryFieldDirective(IObjectType rootQuery, QueryModelRegistration reg)
    {
        // Path: RootQuery.discover -> (DiscoverQueries[.namespace]).<fieldName>
        var discoverField = rootQuery.Fields.FirstOrDefault(f => f.Name == "discover");
        if (discoverField is null)
            throw new InvalidOperationException(
                $"[TraxAuthorize] invariant violated: the `discover` field is not "
                    + $"present on RootQuery, so the entry point for "
                    + $"'{reg.EntityType.FullName}' cannot be located. Likely a "
                    + "ConfigureSchema callback has removed the discover namespace."
            );

        var discoverType = (IObjectType)discoverField.Type.NamedType();
        var container = discoverType;

        if (reg.Attribute.Namespace is not null)
        {
            var nsFieldName = TrainTypeModule.CamelCase(reg.Attribute.Namespace);
            var nsField = discoverType.Fields.FirstOrDefault(f => f.Name == nsFieldName);
            if (nsField is null)
                throw new InvalidOperationException(
                    $"[TraxAuthorize] invariant violated: namespace field "
                        + $"'{nsFieldName}' is missing under `discover` for "
                        + $"'{reg.EntityType.FullName}'."
                );
            container = (IObjectType)nsField.Type.NamedType();
        }

        var fieldName =
            reg.Attribute.Name ?? QueryModelTypeModule.DeriveModelName(reg.EntityType.Name);
        var entryField = container.Fields.FirstOrDefault(f => f.Name == fieldName);
        if (entryField is null)
            throw new InvalidOperationException(
                $"[TraxAuthorize] invariant violated: entry field '{fieldName}' is "
                    + $"missing under `discover` for '{reg.EntityType.FullName}'."
            );

        if (!HasAuthorizeDirective(entryField.Directives))
            throw new InvalidOperationException(
                $"[TraxAuthorize] invariant violated: entry field '{fieldName}' for "
                    + $"'{reg.EntityType.FullName}' has no @authorize directive in the "
                    + "built schema. Without it, an unauthorized caller can read "
                    + "Connection-shaped scalars like `totalCount` and `pageInfo` "
                    + "even though they never resolve a node of the gated type. "
                    + "A ConfigureSchema callback has stripped the directive — "
                    + "remove the override, or remove [TraxAuthorize] from the "
                    + "entity if the exposure is intentional."
            );
    }

    private static bool HasAuthorizeDirective(IDirectiveCollection directives) =>
        directives.Any(d => d.Type.Name == "authorize");

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
