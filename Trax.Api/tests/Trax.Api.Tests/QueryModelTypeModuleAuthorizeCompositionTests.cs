using System.ComponentModel.DataAnnotations.Schema;
using FluentAssertions;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Types;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.Configuration;
using Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;
using Trax.Api.GraphQL.Queries;
using Trax.Api.GraphQL.TypeModules;
using Trax.Effect.Attributes;

namespace Trax.Api.Tests;

/// <summary>
/// Coverage for the corners of <see cref="QueryModelTypeModule.ConfigureField"/>
/// and <see cref="QueryModelTypeModule.CreateObjectType"/> that compose
/// <c>[TraxAuthorize]</c> with the other <c>[TraxQueryModel]</c> knobs.
/// <list type="bullet">
/// <item><c>DeprecationReason</c> is read and emitted as <c>@deprecated</c> on
/// the entry field — a regression that drops the call would silently lose the
/// schema-level deprecation marker.</item>
/// <item><c>BindFields = Explicit</c> + <c>[TraxAuthorize]</c> compose without
/// dropping the directive. The bind-explicit branch is a separate code path
/// from the implicit / ExposeAs branches that the other tests cover.</item>
/// </list>
/// </summary>
[TestFixture]
public class QueryModelTypeModuleAuthorizeCompositionTests
{
    // ── DeprecationReason wiring ────────────────────────────────────────

    [TraxAllowAnonymous]
    [TraxQueryModel(DeprecationReason = "use NewThing instead")]
    [Table("legacy_things", Schema = "test_tm")]
    private class LegacyThing
    {
        [Column("id")]
        public long Id { get; set; }

        [Column("name")]
        public string Name { get; set; } = "";
    }

    private class LegacyDbContext(DbContextOptions<LegacyDbContext> options) : DbContext(options)
    {
        public DbSet<LegacyThing> Things { get; set; } = null!;
    }

    [Test]
    public async Task ConfigureField_DeprecationReasonSet_EntryFieldCarriesDeprecation()
    {
        var schema = await BuildSchemaAsync<LegacyDbContext>();

        var discoverField = schema.QueryType.Fields.Single(f => f.Name == "discover");
        var discoverType = (IObjectType)discoverField.Type.NamedType();
        var entry = discoverType.Fields.Single(f => f.Name == "legacyThings");

        entry.IsDeprecated.Should().BeTrue();
        entry.DeprecationReason.Should().Be("use NewThing instead");
    }

    // ── BindFields.Explicit + [TraxAuthorize] compose ────────────────────

    [TraxQueryModel(BindFields = FieldBindingBehavior.Explicit)]
    [TraxAuthorize(Roles = "Admin")]
    [Table("audit_rows", Schema = "test_tm")]
    private class AuditRow
    {
        [Column("id")]
        public long Id { get; set; }

        [Column("public_name")]
        public string PublicName { get; set; } = "";

        // No [Column] — under Explicit binding this property must be hidden
        // from the GraphQL schema. This is the property that proves the
        // explicit-binding branch ran at all (vs. the implicit fallback that
        // would have surfaced it).
        public string InternalSecret { get; set; } = "";
    }

    private class AuditDbContext(DbContextOptions<AuditDbContext> options) : DbContext(options)
    {
        public DbSet<AuditRow> Rows { get; set; } = null!;
    }

    [Test]
    public async Task CreateObjectType_ExplicitBindingWithAuthorize_HidesNonColumnPropertiesAndEmitsDirective()
    {
        var schema = await BuildSchemaAsync<AuditDbContext>();

        var auditType = schema
            .Types.OfType<IObjectType>()
            .First(t => t.RuntimeType == typeof(AuditRow));

        // Only [Column]-decorated properties appear. If the BindFields.Explicit
        // branch regressed to fall through to implicit binding, InternalSecret
        // would leak in. (`__typename` is HC's reserved introspection field
        // and is filtered here to focus the assertion on entity-shape fields.)
        var fieldNames = auditType
            .Fields.Where(f => !f.IsIntrospectionField)
            .Select(f => f.Name)
            .Order()
            .ToArray();
        fieldNames.Should().BeEquivalentTo(new[] { "id", "publicName" });

        // The @authorize directive must STILL be attached. The bind-explicit
        // branch is a separate descriptor configuration path from the implicit
        // one — a regression that called .Authorize() only in the implicit
        // factory would silently un-gate every entity that uses explicit
        // binding.
        auditType.Directives.Any(d => d.Type.Name == "authorize").Should().BeTrue();
    }

    // ── Schema-build helper ─────────────────────────────────────────────

    /// <summary>
    /// Builds a minimal HotChocolate schema using <see cref="QueryModelTypeModule"/>
    /// against an in-memory EF context. Avoids the full <c>AddTraxGraphQL</c>
    /// dependency graph (TraxMarker, train discovery, etc.) so the test
    /// focuses on the type module's emission behavior.
    /// </summary>
    private static async Task<ISchema> BuildSchemaAsync<TContext>()
        where TContext : DbContext
    {
        var services = new ServiceCollection();
        services.AddDbContext<TContext>(o => o.UseInMemoryDatabase("tmcomp_" + Guid.NewGuid()));

        var builder = new TraxGraphQLBuilder(services);
        builder.AddDbContext<TContext>();
        var config = builder.Build();

        services.AddSingleton(config);
        services.AddSingleton<QueryModelTypeModule>();

        services
            .AddGraphQLServer()
            .AddAuthorization()
            .AddQueryType<TestRootQuery>()
            .AddType<DiscoverQueriesType>()
            .AddTypeModule<QueryModelTypeModule>()
            .AddFiltering()
            .AddSorting()
            .AddProjections();

        var provider = services.BuildServiceProvider();
        var resolver = provider.GetRequiredService<IRequestExecutorResolver>();
        var executor = await resolver.GetRequestExecutorAsync();
        return executor.Schema;
    }

    public class TestRootQuery
    {
        public DiscoverQueries Discover() => new();
    }

    public class DiscoverQueriesType : ObjectType<DiscoverQueries>
    {
        protected override void Configure(IObjectTypeDescriptor<DiscoverQueries> descriptor)
        {
            descriptor.Name("DiscoverQueries");
        }
    }
}
