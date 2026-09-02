using System.ComponentModel.DataAnnotations.Schema;
using FluentAssertions;
using HotChocolate;
using HotChocolate.Data.Filters;
using HotChocolate.Data.Sorting;
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
/// Coverage for <c>[TraxQueryModel(ExposeAs = typeof(I...))]</c>. The feature
/// constrains the GraphQL surface to the property set declared by a "reference"
/// interface (typical use: a scalar-only projection of a cross-schema entity).
/// Tests cover the attribute defaults, build-time validation, the
/// <see cref="QueryModelTypeModule.GetExposedPropertyNames"/> helper, and the
/// end-to-end schema produced by a fully-wired HotChocolate executor.
/// </summary>
[TestFixture]
public class ExposeAsTests
{
    #region Attribute defaults

    [Test]
    public void TraxQueryModelAttribute_ExposeAs_DefaultsToNull()
    {
        new TraxQueryModelAttribute().ExposeAs.Should().BeNull();
    }

    [Test]
    public void TraxQueryModelAttribute_ExposeAs_SetsCorrectly()
    {
        var attr = new TraxQueryModelAttribute { ExposeAs = typeof(IBookReference) };
        attr.ExposeAs.Should().Be(typeof(IBookReference));
    }

    [Test]
    public void TraxQueryModelAttribute_ExposeAs_CoexistsWithOtherSettings()
    {
        var attr = new TraxQueryModelAttribute
        {
            Name = "books",
            Description = "Books",
            Namespace = "library",
            ExposeAs = typeof(IBookReference),
            Paging = true,
            Filtering = true,
            Sorting = true,
            Projection = true,
        };

        attr.Name.Should().Be("books");
        attr.Description.Should().Be("Books");
        attr.Namespace.Should().Be("library");
        attr.ExposeAs.Should().Be(typeof(IBookReference));
    }

    #endregion

    #region GetExposedPropertyNames

    [Test]
    public void GetExposedPropertyNames_DirectProperties_AllReturned()
    {
        var names = QueryModelTypeModule.GetExposedPropertyNames(typeof(IBookReference));
        names.Should().BeEquivalentTo("Id", "Title", "Author", "Rating");
    }

    [Test]
    public void GetExposedPropertyNames_InheritedInterface_FlattensHierarchy()
    {
        var names = QueryModelTypeModule.GetExposedPropertyNames(typeof(IBookReferenceWithAudit));

        // IBookReferenceWithAudit : IBookReference, IAuditReference
        // IAuditReference declares CreatedAt and UpdatedAt
        names.Should().Contain("Id");
        names.Should().Contain("Title");
        names.Should().Contain("CreatedAt");
        names.Should().Contain("UpdatedAt");
    }

    [Test]
    public void GetExposedPropertyNames_DiamondInheritance_Deduplicates()
    {
        // IDiamondLeaf inherits from two paths that both transitively reach IBookReference
        var names = QueryModelTypeModule.GetExposedPropertyNames(typeof(IDiamondLeaf));

        names.Where(n => n == "Id").Should().HaveCount(1, "HashSet should deduplicate");
        names.Should().Contain("LeafProp");
        names.Should().Contain("Id");
    }

    [Test]
    public void GetExposedPropertyNames_EmptyInterface_ReturnsEmpty()
    {
        QueryModelTypeModule.GetExposedPropertyNames(typeof(IEmpty)).Should().BeEmpty();
    }

    [Test]
    public void GetExposedPropertyNames_NoSideEffectOnAttribute()
    {
        // Sanity: the helper is pure — repeated calls return equal sets.
        var first = QueryModelTypeModule.GetExposedPropertyNames(typeof(IBookReference));
        var second = QueryModelTypeModule.GetExposedPropertyNames(typeof(IBookReference));
        first.Should().BeEquivalentTo(second);
    }

    #endregion

    #region Build validation — happy path

    [Test]
    public void Build_ExposeAsValid_PreservesOnRegistration()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());
        builder.AddDbContext<BookDbContext>();

        var config = builder.Build();

        config.ModelRegistrations.Should().HaveCount(1);
        config.ModelRegistrations[0].Attribute.ExposeAs.Should().Be(typeof(IBookReference));
    }

    [Test]
    public void Build_NoExposeAs_RegistrationHasNullExposeAs()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());
        builder.AddDbContext<TestDbContext>();

        var config = builder.Build();

        config.ModelRegistrations[0].Attribute.ExposeAs.Should().BeNull();
    }

    [Test]
    public void Build_ExposeAsWithInheritedInterface_Validates()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());
        builder.AddDbContext<AuditedBookDbContext>();

        var act = () => builder.Build();

        act.Should().NotThrow();
    }

    #endregion

    #region Build validation — failure modes

    [Test]
    public void Build_ExposeAsCombinedWithExplicitBindFields_Throws()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());
        builder.AddDbContext<ExposeAsPlusExplicitDbContext>();

        var act = () => builder.Build();

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*BindFields = Explicit*ExposeAs*choose one*");
    }

    [Test]
    public void Build_ExposeAsClass_Throws()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());
        builder.AddDbContext<ExposeAsClassDbContext>();

        var act = () => builder.Build();

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*must reference an interface*");
    }

    [Test]
    public void Build_EntityDoesNotImplementInterface_Throws()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());
        builder.AddDbContext<UnimplementedInterfaceDbContext>();

        var act = () => builder.Build();

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*does not implement*IBookReference*");
    }

    [Test]
    public void Build_ExposeAsEmptyInterface_Throws()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());
        builder.AddDbContext<EmptyInterfaceDbContext>();

        var act = () => builder.Build();

        act.Should().Throw<InvalidOperationException>().WithMessage("*declares no properties*");
    }

    [Test]
    public void Build_ExposeAsExplicitImplementation_Throws()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());
        builder.AddDbContext<ExplicitImplDbContext>();

        var act = () => builder.Build();

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*ExposeAs requires implicit interface implementations*");
    }

    [Test]
    public void Build_ExposeAsValidationFailureMentionsEntityAndInterface()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());
        builder.AddDbContext<UnimplementedInterfaceDbContext>();

        var act = () => builder.Build();

        var ex = act.Should().Throw<InvalidOperationException>().Subject.First();

        // The error message must name both the entity and the interface so the
        // user can locate the misconfigured attribute without grepping.
        ex.Message.Should().Contain(nameof(MisconfiguredBook));
        ex.Message.Should().Contain(nameof(IBookReference));
    }

    #endregion

    #region CreateTypesAsync — ObjectType naming

    [Test]
    public async Task CreateTypesAsync_ExposeAs_ObjectTypeStillBoundToEntity()
    {
        // The GraphQL type name should derive from the entity (e.g. "BookRef"),
        // not the interface — consumers of the schema see `type BookRef`.
        var config = new GraphQLConfiguration(
            [
                new QueryModelRegistration(
                    typeof(BookRef),
                    typeof(BookDbContext),
                    new TraxQueryModelAttribute { ExposeAs = typeof(IBookReference) }
                ),
            ],
            [],
            [],
            []
        );
        var module = new QueryModelTypeModule(config);

        var types = await module.CreateTypesAsync(null!, CancellationToken.None);

        types
            .Should()
            .ContainSingle(t =>
                t.GetType().IsGenericType
                && t.GetType().GetGenericTypeDefinition() == typeof(ObjectType<>)
                && t.GetType().GetGenericArguments()[0] == typeof(BookRef)
            );
    }

    #endregion

    #region Schema integration — entity object type

    [Test]
    public async Task Schema_WithExposeAs_OnlyExposesInterfaceProperties()
    {
        var schema = await BuildSchemaAsync<BookDbContext>();

        var bookType = schema.Types.GetType<ObjectType>("BookRef");

        var fieldNames = bookType
            .Fields.Where(f => !f.IsIntrospectionField)
            .Select(f => f.Name)
            .ToHashSet();

        fieldNames.Should().BeEquivalentTo("id", "title", "author", "rating");
    }

    [Test]
    public async Task Schema_WithExposeAs_HiddenNavPropertiesNotPresent()
    {
        var schema = await BuildSchemaAsync<BookDbContext>();

        var bookType = schema.Types.GetType<ObjectType>("BookRef");

        bookType
            .Fields.Should()
            .NotContain(f => f.Name == "sponsors", "ExposeAs must hide nav properties");
        bookType.Fields.Should().NotContain(f => f.Name == "internalNotes");
    }

    [Test]
    public async Task Schema_WithoutExposeAs_ExposesAllPublicProperties()
    {
        var schema = await BuildSchemaAsync<UnrestrictedBookDbContext>();

        var bookType = schema.Types.GetType<ObjectType>("UnrestrictedBookRef");

        var fieldNames = bookType
            .Fields.Where(f => !f.IsIntrospectionField)
            .Select(f => f.Name)
            .ToHashSet();

        // Without ExposeAs, HC binds every public property — including
        // navs and internal columns. This is the "default lies" baseline
        // that ExposeAs solves.
        fieldNames.Should().Contain("internalNotes");
    }

    [Test]
    public async Task Schema_WithExposeAsInheritedInterface_ExposesInheritedFields()
    {
        var schema = await BuildSchemaAsync<AuditedBookDbContext>();

        var bookType = schema.Types.GetType<ObjectType>("AuditedBookRef");

        var fieldNames = bookType
            .Fields.Where(f => !f.IsIntrospectionField)
            .Select(f => f.Name)
            .ToHashSet();

        // Fields from both the leaf and the inherited IAuditReference must appear.
        fieldNames.Should().Contain("id");
        fieldNames.Should().Contain("title");
        fieldNames.Should().Contain("createdAt");
        fieldNames.Should().Contain("updatedAt");
        fieldNames.Should().NotContain("internalNotes");
    }

    #endregion

    #region Schema integration — filter input type

    [Test]
    public async Task Schema_WithExposeAs_FilterInputRestrictedToInterface()
    {
        var schema = await BuildSchemaAsync<BookDbContext>();

        var filterType = schema.Types.GetType<InputObjectType>("BookRefFilterInput");

        var fieldNames = filterType.Fields.Select(f => f.Name).ToHashSet();

        // HC includes "and"/"or" automatically — strip those and check the rest.
        var dataFields = fieldNames.Where(n => n != "and" && n != "or").ToHashSet();

        dataFields.Should().BeEquivalentTo("id", "title", "author", "rating");
        dataFields.Should().NotContain("internalNotes");
    }

    [Test]
    public async Task Schema_WithExposeAs_AndExplicitFilterOverride_OverrideWins()
    {
        var schema = await BuildSchemaAsync<BookDbContext>(builder =>
            builder.AddFilterType<BookRef, BookRefIdOnlyFilter>()
        );

        var filterType = schema.Types.GetType<InputObjectType>("BookRefIdOnlyFilter");

        var fieldNames = filterType.Fields.Select(f => f.Name).ToHashSet();
        var dataFields = fieldNames.Where(n => n != "and" && n != "or").ToHashSet();

        // The override declares only `id`, so the schema should reflect that
        // even though ExposeAs would otherwise expose four fields.
        dataFields.Should().BeEquivalentTo("id");
    }

    [Test]
    public async Task Schema_WithoutExposeAs_FilterInputExposesAllProperties()
    {
        var schema = await BuildSchemaAsync<UnrestrictedBookDbContext>();

        var filterType = schema.Types.GetType<InputObjectType>("UnrestrictedBookRefFilterInput");

        var fieldNames = filterType.Fields.Select(f => f.Name).ToHashSet();

        // Baseline: without ExposeAs, the hidden column is filterable.
        fieldNames.Should().Contain("internalNotes");
    }

    #endregion

    #region Schema integration — sort input type

    [Test]
    public async Task Schema_WithExposeAs_SortInputRestrictedToInterface()
    {
        var schema = await BuildSchemaAsync<BookDbContext>();

        var sortType = schema.Types.GetType<InputObjectType>("BookRefSortInput");

        var fieldNames = sortType.Fields.Select(f => f.Name).ToHashSet();

        fieldNames.Should().BeEquivalentTo("id", "title", "author", "rating");
        fieldNames.Should().NotContain("internalNotes");
    }

    [Test]
    public async Task Schema_WithExposeAs_AndExplicitSortOverride_OverrideWins()
    {
        var schema = await BuildSchemaAsync<BookDbContext>(builder =>
            builder.AddSortType<BookRef, BookRefTitleOnlySort>()
        );

        var sortType = schema.Types.GetType<InputObjectType>("BookRefTitleOnlySort");
        var fieldNames = sortType.Fields.Select(f => f.Name).ToHashSet();

        fieldNames.Should().BeEquivalentTo("title");
    }

    #endregion

    #region Schema integration — query parses & rejects hidden fields

    [Test]
    public async Task QueryHiddenField_FailsAtValidation()
    {
        var executor = await BuildExecutorAsync<BookDbContext>();

        var result = await executor.ExecuteAsync(
            "{ discover { bookRefs { nodes { id internalNotes } } } }"
        );

        var operationResult = result.ExpectOperationResult();
        operationResult.Errors.Should().NotBeNullOrEmpty();
        operationResult
            .Errors!.Should()
            .Contain(e => e.Message.Contains("internalNotes") || e.Message.Contains("field"));
    }

    [Test]
    public async Task QueryAllowedFields_Succeeds()
    {
        var executor = await BuildExecutorAsync<BookDbContext>(seedData: true);

        var result = await executor.ExecuteAsync(
            "{ discover { bookRefs { totalCount nodes { id title author rating } } } }"
        );

        var operationResult = result.ExpectOperationResult();
        operationResult.Errors.Should().BeNullOrEmpty();
        operationResult.Data.Should().NotBeNull();
    }

    [Test]
    public async Task QueryFilteringOnAllowedField_Succeeds()
    {
        var executor = await BuildExecutorAsync<BookDbContext>(seedData: true);

        var result = await executor.ExecuteAsync(
            "{ discover { bookRefs(where: { rating: { eq: 5 } }) { totalCount } } }"
        );

        var operationResult = result.ExpectOperationResult();
        operationResult.Errors.Should().BeNullOrEmpty();
    }

    [Test]
    public async Task QueryFilteringOnHiddenField_FailsAtValidation()
    {
        var executor = await BuildExecutorAsync<BookDbContext>();

        var result = await executor.ExecuteAsync(
            "{ discover { bookRefs(where: { internalNotes: { eq: \"x\" } }) { totalCount } } }"
        );

        var operationResult = result.ExpectOperationResult();
        operationResult.Errors.Should().NotBeNullOrEmpty();
    }

    [Test]
    public async Task QuerySortingOnHiddenField_FailsAtValidation()
    {
        var executor = await BuildExecutorAsync<BookDbContext>();

        var result = await executor.ExecuteAsync(
            "{ discover { bookRefs(order: { internalNotes: ASC }) { totalCount } } }"
        );

        var operationResult = result.ExpectOperationResult();
        operationResult.Errors.Should().NotBeNullOrEmpty();
    }

    [Test]
    public async Task QuerySortingOnAllowedField_Succeeds()
    {
        var executor = await BuildExecutorAsync<BookDbContext>(seedData: true);

        var result = await executor.ExecuteAsync(
            "{ discover { bookRefs(order: { title: ASC }) { nodes { title } } } }"
        );

        var operationResult = result.ExpectOperationResult();
        operationResult.Errors.Should().BeNullOrEmpty();
    }

    #endregion

    #region Helpers — schema construction

    private static async Task<ISchemaDefinition> BuildSchemaAsync<TContext>(
        Action<TraxGraphQLBuilder>? customize = null
    )
        where TContext : DbContext
    {
        var executor = await BuildExecutorAsync<TContext>(customize, seedData: false);
        return executor.Schema;
    }

    private static async Task<IRequestExecutor> BuildExecutorAsync<TContext>(
        Action<TraxGraphQLBuilder>? customize = null,
        bool seedData = false
    )
        where TContext : DbContext
    {
        var services = new ServiceCollection();
        services.AddDbContext<TContext>(o =>
            o.UseInMemoryDatabase("ExposeAsTest_" + Guid.NewGuid())
        );

        // Build the QueryModelTypeModule configuration manually so we don't
        // need to wire AddTraxGraphQL (and its TraxMarker / IEffectRegistry /
        // ITrainDiscoveryService dependencies).
        var builder = new TraxGraphQLBuilder(services);
        builder.AddDbContext<TContext>();
        customize?.Invoke(builder);
        var config = builder.Build();

        services.AddSingleton(config);
        services.AddSingleton<QueryModelTypeModule>();
        services.AddSingleton<RegisteredNamespaceTracker>();

        services
            .AddGraphQLServer()
            .AddQueryType<TestRootQuery>()
            .AddType<DiscoverQueriesType>()
            .AddTypeModule<QueryModelTypeModule>()
            .AddFiltering()
            .AddSorting()
            .AddProjections();

        var provider = services.BuildServiceProvider();

        if (seedData && typeof(TContext) == typeof(BookDbContext))
        {
            var ctx = (BookDbContext)(object)provider.GetRequiredService<TContext>();
            ctx.Books.AddRange(
                new BookRef
                {
                    Id = 1,
                    Title = "A",
                    Author = "X",
                    Rating = 5,
                    InternalNotes = "n1",
                },
                new BookRef
                {
                    Id = 2,
                    Title = "B",
                    Author = "Y",
                    Rating = 4,
                    InternalNotes = "n2",
                }
            );
            await ctx.SaveChangesAsync();
        }

        return await provider.GetRequiredService<IRequestExecutorProvider>().GetExecutorAsync();
    }

    #endregion

    #region Test query types — minimal RootQuery exposing `discover`

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

    // Bookkeeping singleton so multiple QueryModelTypeModule instances built
    // in a single test process don't share namespace registration state.
    public class RegisteredNamespaceTracker;

    #endregion
}

#region Test interfaces

public interface IBookReference
{
    int Id { get; }
    string Title { get; }
    string Author { get; }
    int Rating { get; }
}

public interface IAuditReference
{
    DateTime CreatedAt { get; }
    DateTime UpdatedAt { get; }
}

public interface IBookReferenceWithAudit : IBookReference, IAuditReference;

public interface IEmpty;

public interface IDiamondLeftBranch : IBookReference
{
    string LeftProp { get; }
}

public interface IDiamondRightBranch : IBookReference
{
    string RightProp { get; }
}

public interface IDiamondLeaf : IDiamondLeftBranch, IDiamondRightBranch
{
    string LeafProp { get; }
}

public interface IBookExplicit
{
    int Id { get; }
    string SecretField { get; }
}

#endregion

#region Test entities

[TraxAllowAnonymous]
[TraxQueryModel(Name = "bookRefs", ExposeAs = typeof(IBookReference))]
public class BookRef : IBookReference
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Author { get; set; } = "";
    public int Rating { get; set; }

    /// This represents the "hidden" nav-like property that ExposeAs must keep out of the schema.
    public string InternalNotes { get; set; } = "";
}

[TraxAllowAnonymous]
[TraxQueryModel(Name = "auditedBookRefs", ExposeAs = typeof(IBookReferenceWithAudit))]
public class AuditedBookRef : IBookReferenceWithAudit
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Author { get; set; } = "";
    public int Rating { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string InternalNotes { get; set; } = "";
}

[TraxAllowAnonymous]
[TraxQueryModel(Name = "unrestrictedBookRefs")]
public class UnrestrictedBookRef
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string InternalNotes { get; set; } = "";
}

[TraxQueryModel(ExposeAs = typeof(IBookReference), BindFields = FieldBindingBehavior.Explicit)]
public class ConflictBook : IBookReference
{
    [Column("id")]
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Author { get; set; } = "";
    public int Rating { get; set; }
}

// Targeting a non-interface type for ExposeAs is illegal.
[TraxQueryModel(ExposeAs = typeof(SomeBaseClass))]
public class WrongExposeAsKind : SomeBaseClass
{
    public int Id { get; set; }
}

public class SomeBaseClass
{
    public int BaseProp { get; set; }
}

[TraxQueryModel(ExposeAs = typeof(IBookReference))]
public class MisconfiguredBook
{
    // Intentionally does NOT implement IBookReference.
    public int Id { get; set; }
    public string Title { get; set; } = "";
}

[TraxQueryModel(ExposeAs = typeof(IEmpty))]
public class EmptyExposed : IEmpty
{
    public int Id { get; set; }
}

[TraxQueryModel(ExposeAs = typeof(IBookExplicit))]
public class ExplicitImplBook : IBookExplicit
{
    public int Id { get; set; }

    // Explicit interface impl — no public `SecretField` property exists on
    // ExplicitImplBook, so the GraphQL field can't be named.
    string IBookExplicit.SecretField => "secret";
}

#endregion

#region Test DbContexts

public class BookDbContext : DbContext
{
    public DbSet<BookRef> Books { get; set; } = null!;

    public BookDbContext(DbContextOptions<BookDbContext> options)
        : base(options) { }

    public BookDbContext()
    {
        // Required for the manual instantiation paths some tests use.
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
            optionsBuilder.UseInMemoryDatabase("BookDb_" + Guid.NewGuid());
    }
}

public class AuditedBookDbContext : DbContext
{
    public DbSet<AuditedBookRef> Books { get; set; } = null!;

    public AuditedBookDbContext(DbContextOptions<AuditedBookDbContext> options)
        : base(options) { }

    public AuditedBookDbContext() { }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
            optionsBuilder.UseInMemoryDatabase("AuditedBookDb_" + Guid.NewGuid());
    }
}

public class UnrestrictedBookDbContext : DbContext
{
    public DbSet<UnrestrictedBookRef> Books { get; set; } = null!;

    public UnrestrictedBookDbContext(DbContextOptions<UnrestrictedBookDbContext> options)
        : base(options) { }

    public UnrestrictedBookDbContext() { }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
            optionsBuilder.UseInMemoryDatabase("UnrestrictedBookDb_" + Guid.NewGuid());
    }
}

public class ExposeAsPlusExplicitDbContext : DbContext
{
    public DbSet<ConflictBook> Books { get; set; } = null!;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseInMemoryDatabase("ConflictDb_" + Guid.NewGuid());
}

public class ExposeAsClassDbContext : DbContext
{
    public DbSet<WrongExposeAsKind> Items { get; set; } = null!;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseInMemoryDatabase("WrongKindDb_" + Guid.NewGuid());
}

public class UnimplementedInterfaceDbContext : DbContext
{
    public DbSet<MisconfiguredBook> Books { get; set; } = null!;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseInMemoryDatabase("MisconfDb_" + Guid.NewGuid());
}

public class EmptyInterfaceDbContext : DbContext
{
    public DbSet<EmptyExposed> Items { get; set; } = null!;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseInMemoryDatabase("EmptyDb_" + Guid.NewGuid());
}

public class ExplicitImplDbContext : DbContext
{
    public DbSet<ExplicitImplBook> Books { get; set; } = null!;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseInMemoryDatabase("ExplicitImplDb_" + Guid.NewGuid());
}

#endregion

#region Test filter / sort overrides

public class BookRefIdOnlyFilter : FilterInputType<BookRef>
{
    protected override void Configure(IFilterInputTypeDescriptor<BookRef> descriptor)
    {
        descriptor.Name("BookRefIdOnlyFilter");
        descriptor.BindFieldsExplicitly();
        descriptor.Field(x => x.Id);
    }
}

public class BookRefTitleOnlySort : SortInputType<BookRef>
{
    protected override void Configure(ISortInputTypeDescriptor<BookRef> descriptor)
    {
        descriptor.Name("BookRefTitleOnlySort");
        descriptor.BindFieldsExplicitly();
        descriptor.Field(x => x.Title);
    }
}

#endregion
