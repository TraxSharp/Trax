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
/// Type-module emission coverage for <c>[TraxAllowAnonymous]</c>. The four
/// <c>@authorize</c> emission sites in <see cref="QueryModelTypeModule"/>
/// (entry field, ExposeAs ObjectType branch, implicit-bind ObjectType branch,
/// explicit-bind ObjectType branch) must all skip emission when the
/// registration carries <c>AllowAnonymous = true</c>. A regression that drops
/// the skip on any one site re-locks the surface the user explicitly opened.
/// </summary>
[TestFixture]
public class QueryModelTypeModuleAllowAnonymousCompositionTests
{
    // ── Default bind (implicit) ──────────────────────────────────────────

    [TraxQueryModel]
    [TraxAllowAnonymous]
    [Table("anon_default_rows", Schema = "test_tm_anon")]
    private class AnonDefaultRow
    {
        [Column("id")]
        public long Id { get; set; }

        [Column("title")]
        public string Title { get; set; } = "";
    }

    private class AnonDefaultDbContext(DbContextOptions<AnonDefaultDbContext> options)
        : DbContext(options)
    {
        public DbSet<AnonDefaultRow> Rows { get; set; } = null!;
    }

    [Test]
    public async Task ConfigureField_AllowAnonymous_EntryFieldHasNoAuthorizeDirective()
    {
        var schema = await BuildSchemaAsync<AnonDefaultDbContext>();

        var discoverField = schema.QueryType.Fields.Single(f => f.Name == "discover");
        var discoverType = (IObjectType)discoverField.Type.NamedType();
        var entry = discoverType.Fields.Single(f => f.Name == "anonDefaultRows");

        entry
            .Directives.Any(d => d.Type.Name == "authorize")
            .Should()
            .BeFalse("an anonymous entry field must not carry the @authorize directive");
    }

    [Test]
    public async Task CreateObjectType_AllowAnonymous_ObjectTypeHasNoAuthorizeDirective()
    {
        var schema = await BuildSchemaAsync<AnonDefaultDbContext>();

        var anonType = schema
            .Types.OfType<IObjectType>()
            .First(t => t.RuntimeType == typeof(AnonDefaultRow));

        anonType
            .Directives.Any(d => d.Type.Name == "authorize")
            .Should()
            .BeFalse("the ObjectType of an anonymous entity must not carry @authorize");
    }

    // ── BindFields.Explicit branch ───────────────────────────────────────

    [TraxQueryModel(BindFields = FieldBindingBehavior.Explicit)]
    [TraxAllowAnonymous]
    [Table("anon_explicit_rows", Schema = "test_tm_anon")]
    private class AnonExplicitRow
    {
        [Column("id")]
        public long Id { get; set; }

        [Column("public_field")]
        public string PublicField { get; set; } = "";

        // No [Column] — must stay hidden under Explicit binding.
        public string InternalDetail { get; set; } = "";
    }

    private class AnonExplicitDbContext(DbContextOptions<AnonExplicitDbContext> options)
        : DbContext(options)
    {
        public DbSet<AnonExplicitRow> Rows { get; set; } = null!;
    }

    [Test]
    public async Task CreateObjectType_AllowAnonymousWithExplicitBinding_HidesUntaggedFieldsAndOmitsAuthorize()
    {
        // The explicit-binding code path is a separate descriptor configuration
        // branch from the implicit one. A regression that called .Authorize()
        // there but not the implicit branch (or vice versa) would silently
        // re-gate explicit-binding anonymous entities.
        var schema = await BuildSchemaAsync<AnonExplicitDbContext>();

        var anonType = schema
            .Types.OfType<IObjectType>()
            .First(t => t.RuntimeType == typeof(AnonExplicitRow));

        var fieldNames = anonType
            .Fields.Where(f => !f.IsIntrospectionField)
            .Select(f => f.Name)
            .Order()
            .ToArray();
        fieldNames.Should().BeEquivalentTo(new[] { "id", "publicField" });

        anonType.Directives.Any(d => d.Type.Name == "authorize").Should().BeFalse();
    }

    // ── ExposeAs branch ──────────────────────────────────────────────────

    private interface IAnonExposed
    {
        long Id { get; }
        string Headline { get; }
    }

    [TraxQueryModel(ExposeAs = typeof(IAnonExposed))]
    [TraxAllowAnonymous]
    [Table("anon_exposed_rows", Schema = "test_tm_anon")]
    private class AnonExposedRow : IAnonExposed
    {
        [Column("id")]
        public long Id { get; set; }

        [Column("headline")]
        public string Headline { get; set; } = "";

        [Column("internal_body")]
        public string InternalBody { get; set; } = "";
    }

    private class AnonExposedDbContext(DbContextOptions<AnonExposedDbContext> options)
        : DbContext(options)
    {
        public DbSet<AnonExposedRow> Rows { get; set; } = null!;
    }

    [Test]
    public async Task CreateObjectType_AllowAnonymousWithExposeAs_NarrowsFieldsAndOmitsAuthorize()
    {
        // ExposeAs hides columns; AllowAnonymous opens row access. The two
        // compose naturally: a public projection of a richer entity. The
        // ExposeAs branch in CreateObjectType is its own emission site, so
        // the skip must apply there too.
        var schema = await BuildSchemaAsync<AnonExposedDbContext>();

        var anonType = schema
            .Types.OfType<IObjectType>()
            .First(t => t.RuntimeType == typeof(AnonExposedRow));

        var fieldNames = anonType
            .Fields.Where(f => !f.IsIntrospectionField)
            .Select(f => f.Name)
            .Order()
            .ToArray();
        fieldNames.Should().BeEquivalentTo(new[] { "headline", "id" });

        anonType.Directives.Any(d => d.Type.Name == "authorize").Should().BeFalse();
    }

    // ── Schema-build helper ─────────────────────────────────────────────

    private static async Task<ISchema> BuildSchemaAsync<TContext>()
        where TContext : DbContext
    {
        var services = new ServiceCollection();
        services.AddDbContext<TContext>(o => o.UseInMemoryDatabase("tmanon_" + Guid.NewGuid()));

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
