using FluentAssertions;
using HotChocolate;
using HotChocolate.Data;
using HotChocolate.Data.Filters;
using HotChocolate.Execution;
using HotChocolate.Types;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Trax.Api.GraphQL.Configuration;
using Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.GraphQL.Filtering.ListElements;
using Trax.Api.GraphQL.Queries;
using Trax.Api.GraphQL.TypeModules;
using Trax.Api.Services.HealthCheck;
using Trax.Effect.Attributes;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.Tests;

/// <summary>
/// Coverage for the restricted list-element filter inputs. HotChocolate reuses one
/// operation filter input for a scalar property and for the elements of a collection
/// property, but <c>neq</c> only translates in the scalar position: inside a collection
/// it lowers to <c>Any(x =&gt; x != value)</c> over a primitive collection, which EF Core
/// cannot translate, so the query passes validation and throws at execution.
/// </summary>
/// <remarks>
/// These tests run over the EF Core InMemory provider, which evaluates any expression in
/// process and so cannot reproduce the translation failure itself. What they pin is the
/// fix: the operation is gone from the element input (and only from there), and every
/// other operation still filters. The Npgsql regions below cover the translation that
/// motivates the change, against the real provider.
/// </remarks>
[TestFixture]
public class ListElementFilterTests
{
    #region Schema — neq is removed, in list position only

    [Test]
    public async Task Schema_EnumListElementInput_HasNoNeq()
    {
        var schema = await BuildSchemaAsync();

        var element = schema.Types.GetType<InputObjectType>("BadgeElementFilterInput");
        var names = element.Fields.Select(f => f.Name).ToHashSet();

        names.Should().NotContain("neq");
        // Everything that does translate is still offered.
        names.Should().Contain(["eq", "in", "nin"]);
    }

    [Test]
    public async Task Schema_ScalarEnumProperty_KeepsNeq()
    {
        // The scalar input is a different type and is untouched: `neq` translates there.
        var schema = await BuildSchemaAsync();

        var scalar = schema.Types.GetType<InputObjectType>("BadgeOperationFilterInput");

        scalar.Fields.Select(f => f.Name).Should().Contain(["eq", "neq", "in", "nin"]);
    }

    [Test]
    public async Task Schema_ListFilterInput_KeepsStockNameAndOperations()
    {
        // The list wrapper keeps HotChocolate's stock name, so the only visible schema
        // change is the missing `neq` on the element type.
        var schema = await BuildSchemaAsync();

        var list = schema.Types.GetType<InputObjectType>("ListBadgeElementFilterInput");

        list.Fields.Select(f => f.Name).Should().BeEquivalentTo(["all", "none", "some", "any"]);
    }

    [Test]
    public async Task Schema_StringAndComparableListElements_HaveNoNeqButKeepTheirOtherOperations()
    {
        var schema = await BuildSchemaAsync();

        var stringElement = schema.Types.GetType<InputObjectType>("StringElementFilterInput");
        var stringNames = stringElement.Fields.Select(f => f.Name).ToHashSet();
        stringNames.Should().NotContain("neq");
        stringNames.Should().Contain(["eq", "contains", "startsWith", "in"]);

        var intElement = schema.Types.GetType<InputObjectType>("IntElementFilterInput");
        var intNames = intElement.Fields.Select(f => f.Name).ToHashSet();
        intNames.Should().NotContain("neq");
        // Comparable operators translate inside a collection and must survive.
        intNames.Should().Contain(["eq", "gt", "gte", "lt", "lte", "in", "nin"]);
    }

    [Test]
    public async Task Schema_ListBackedByListOfT_IsRestrictedToo()
    {
        // The trap is the same whether the property is Badge[] or List<Badge>; both bind
        // to the restricted element input.
        var schema = await BuildSchemaAsync();

        var field = schema
            .Types.GetType<InputObjectType>("PlayerRowFilterInput")
            .Fields.Single(f => f.Name == "legacyBadges");

        field.Type.NamedType().Name.Should().Be("ListBadgeElementFilterInput");
    }

    [Test]
    public async Task Schema_NavigationCollection_IsUntouched()
    {
        // List<Book> is a join, not an array column. Its element filter is the entity's
        // own filter input, where `neq` never appears and nothing needs restricting.
        var schema = await BuildSchemaAsync<LibraryDbContext>();

        var field = schema
            .Types.GetType<InputObjectType>("ShelfFilterInput")
            .Fields.Single(f => f.Name == "books");

        var listType = schema.Types.GetType<InputObjectType>(field.Type.NamedType().Name);
        var some = listType.Fields.Single(f => f.Name == "some");

        // The element is the Book filter, and its scalar fields keep the full operation
        // set including neq.
        some.Type.NamedType().Name.Should().Be("BookFilterInput");
        schema
            .Types.GetType<InputObjectType>("StringOperationFilterInput")
            .Fields.Select(f => f.Name)
            .Should()
            .Contain("neq");
    }

    #endregion

    #region Schema — every scalar element kind on one entity

    // Each bound element type produces its own closed generic filter type, so the names
    // have to be unique across the whole schema. Stock HotChocolate shares one input
    // between float[] and double[] (both are the GraphQL Float scalar) and between
    // DateTime[] and DateTimeOffset[]; the restricted types cannot, and naming them after
    // the GraphQL scalar made the schema fail to build with "The name
    // `ListFloatOperationFilterInput` was already registered by another type".

    [Test]
    public async Task Schema_EveryScalarElementKind_BuildsWithoutNameCollision()
    {
        // Reaching the schema at all is the assertion: a duplicate name throws
        // SchemaException during build and the host never starts.
        var schema = await BuildSchemaAsync<AllScalarKindsDbContext>();

        schema.Types.GetType<InputObjectType>("AllScalarKindsRowFilterInput").Should().NotBeNull();
    }

    [Test]
    public async Task Schema_AliasedScalarPairs_GetDistinctInputTypes()
    {
        var schema = await BuildSchemaAsync<AllScalarKindsDbContext>();

        var filterInput = schema.Types.GetType<InputObjectType>("AllScalarKindsRowFilterInput");

        string ListTypeOf(string field) =>
            filterInput.Fields.Single(f => f.Name == field).Type.NamedType().Name;

        // The pairs stock HotChocolate would have collapsed onto one name.
        ListTypeOf("floats").Should().NotBe(ListTypeOf("doubles"));
        ListTypeOf("stamps").Should().NotBe(ListTypeOf("offsets"));
    }

    [Test]
    public async Task Schema_EveryScalarElementKind_DropsNeqAndKeepsTheRest()
    {
        var schema = await BuildSchemaAsync<AllScalarKindsDbContext>();

        var elementInputs = schema
            .Types.OfType<InputObjectType>()
            // The list wrappers share the suffix but hold all/none/some/any, not operations.
            .Where(t => t.Name.EndsWith("ElementFilterInput") && !t.Name.StartsWith("List"))
            .ToList();

        // One per distinct bound element type on the entity.
        elementInputs.Should().HaveCountGreaterThan(8);

        foreach (var input in elementInputs)
        {
            var names = input.Fields.Select(f => f.Name).ToHashSet();
            names.Should().NotContain("neq", $"{input.Name} is an element input");
            names.Should().Contain("eq", $"{input.Name} must keep the operations that translate");
        }
    }

    [Test]
    public async Task Schema_NullableElement_KeepsStockInputAndIsNotRestricted()
    {
        // Nullable<T> cannot close ComparableOperationFilterInputType<T> (the `struct`
        // constraint excludes it), so these keep HotChocolate's stock element input,
        // `neq` included. Binding one used to throw at startup.
        var schema = await BuildSchemaAsync<AllScalarKindsDbContext>();

        var listType = schema
            .Types.GetType<InputObjectType>("AllScalarKindsRowFilterInput")
            .Fields.Single(f => f.Name == "optionalScores")
            .Type.NamedType()
            .Name;

        var element = schema
            .Types.GetType<InputObjectType>(listType)
            .Fields.Single(f => f.Name == "some")
            .Type.NamedType()
            .Name;

        element.Should().NotEndWith("ElementFilterInput");
        schema
            .Types.GetType<InputObjectType>(element)
            .Fields.Select(f => f.Name)
            .Should()
            .Contain("neq");
    }

    [Test]
    public void Discover_NullableElement_IsNotBound()
    {
        var bindings = ListElementFilterBinding.Discover([typeof(AllScalarKindsRow)]);

        bindings.Should().NotContain(b => b.Key == typeof(int?[]));
        // The non-nullable twin on the same entity still binds.
        bindings.Should().Contain(b => b.Key == typeof(int[]));
    }

    [Test]
    public async Task Schema_RestrictedTypes_DoNotReuseHotChocolateStockNames()
    {
        // A collection whose element cannot be restricted keeps the stock types, so a
        // restricted type must never claim a stock name. int[] (restricted) and int?[]
        // (stock) coexisting used to fail with "The name `ListIntOperationFilterInput`
        // was already registered by another type".
        var schema = await BuildSchemaAsync<AllScalarKindsDbContext>();

        var restricted = schema
            .Types.OfType<InputObjectType>()
            .Where(t => t.Name.EndsWith("ElementFilterInput"))
            .Select(t => t.Name)
            .ToList();

        restricted.Should().NotBeEmpty();
        restricted.Should().OnlyContain(n => !n.EndsWith("OperationFilterInput"));
        restricted.Should().OnlyHaveUniqueItems();
    }

    #endregion

    #region Behavior — the untranslatable operation is rejected before execution

    [Test]
    public async Task SomeNeq_IsRejectedAtValidation()
    {
        var executor = await BuildExecutorAsync(seedData: true);

        var result = await executor.ExecuteAsync(
            "{ discover { players(where: { badges: { some: { neq: VETERAN } } }) { totalCount } } }"
        );

        var op = result.ExpectOperationResult();
        op.Errors.Should().NotBeNullOrEmpty();
        op.Errors!.Should().Contain(e => e.Message.Contains("neq"));
        // Rejected outright rather than reaching a resolver, so `data` is absent.
        op.Data.Should().BeNull();
    }

    [Test]
    public async Task NoneNeq_IsRejectedAtValidation()
    {
        var executor = await BuildExecutorAsync(seedData: true);

        var result = await executor.ExecuteAsync(
            "{ discover { players(where: { badges: { none: { neq: VETERAN } } }) { totalCount } } }"
        );

        var op = result.ExpectOperationResult();
        op.Errors.Should().NotBeNullOrEmpty();
        op.Data.Should().BeNull();
    }

    [Test]
    public async Task ScalarNeq_StillFiltersRows()
    {
        // The whole point of restricting only the element input: scalar neq still works.
        var executor = await BuildExecutorAsync(seedData: true);

        var ids = await QueryIdsAsync(executor, "tier: { neq: FOUNDER }");

        // Every row except 1, which is the only FOUNDER.
        ids.Should().Equal(2, 3, 4);
    }

    #endregion

    #region Behavior — every operation that does translate still filters

    [Test]
    public async Task SomeEq_FiltersRows()
    {
        var executor = await BuildExecutorAsync(seedData: true);

        var ids = await QueryIdsAsync(executor, "badges: { some: { eq: CHAMPION } }");

        ids.Should().Equal(1, 3);
    }

    [Test]
    public async Task AndOfTwoSomeEq_FiltersRows_ContainsAllSemantics()
    {
        var executor = await BuildExecutorAsync(seedData: true);

        var ids = await QueryIdsAsync(
            executor,
            "and: [ { badges: { some: { eq: FOUNDER } } }, { badges: { some: { eq: CHAMPION } } } ]"
        );

        // Only row 1 holds both.
        ids.Should().Equal(1);
    }

    [Test]
    public async Task NoneEq_FiltersRows_AndReplacesAllNeq()
    {
        // `all: { neq: X }` was the operation lost to the restriction; this is the exact
        // equivalent and is still available.
        var executor = await BuildExecutorAsync(seedData: true);

        var ids = await QueryIdsAsync(executor, "badges: { none: { eq: CHAMPION } }");

        ids.Should().Equal(2, 4);
    }

    [Test]
    public async Task AnyFlag_FiltersOnEmptiness()
    {
        var executor = await BuildExecutorAsync(seedData: true);

        (await QueryIdsAsync(executor, "badges: { any: false }")).Should().Equal(4);
        (await QueryIdsAsync(executor, "badges: { any: true }")).Should().Equal(1, 2, 3);
    }

    #endregion

    #region SQL shape — HotChocolate + Npgsql, end to end

    // These run the real HotChocolate filter pipeline over the real Npgsql provider and
    // read back the SQL it compiled. ToQueryString() never opens a connection, so they
    // need no database and cannot flake. This is the layer the restriction is about: the
    // EF Core InMemory provider evaluates everything in process and so can neither
    // reproduce the failure nor prove the operators below.

    [Test]
    public async Task Npgsql_SomeEq_TranslatesToArrayContainment()
    {
        // `contains`: the GIN-indexable operator.
        var (sql, errors) = await NpgsqlFilterAsync("badges: { some: { eq: CHAMPION } }");

        errors.Should().BeNullOrEmpty();
        sql.Should().Contain("@>");
    }

    [Test]
    public async Task Npgsql_SomeIn_TranslatesToArrayOverlap()
    {
        // `containsAny`: also GIN-indexable.
        var (sql, errors) = await NpgsqlFilterAsync(
            "badges: { some: { in: [CHAMPION, VETERAN] } }"
        );

        errors.Should().BeNullOrEmpty();
        sql.Should().Contain("&&");
    }

    [Test]
    public async Task Npgsql_AndOfTwoSomeEq_TranslatesToTwoContainments()
    {
        // `containsAll`: two GIN-indexable predicates the planner can combine.
        var (sql, errors) = await NpgsqlFilterAsync(
            "and: [ { badges: { some: { eq: CHAMPION } } }, { badges: { some: { eq: VETERAN } } } ]"
        );

        errors.Should().BeNullOrEmpty();
        sql.Should().Contain("@>");
        System
            .Text.RegularExpressions.Regex.Matches(sql!, "@>")
            .Count.Should()
            .Be(2, "each membership test contributes its own containment predicate");
    }

    [Test]
    public async Task Npgsql_AllIn_TranslatesToContainedBy()
    {
        var (sql, errors) = await NpgsqlFilterAsync("badges: { all: { in: [CHAMPION] } }");

        errors.Should().BeNullOrEmpty();
        sql.Should().Contain("<@");
    }

    [Test]
    public async Task Npgsql_NoneEq_TranslatesToNegatedContainment()
    {
        var (sql, errors) = await NpgsqlFilterAsync("badges: { none: { eq: CHAMPION } }");

        errors.Should().BeNullOrEmpty();
        sql.Should().Contain("@>");
        sql.Should().Contain("NOT");
    }

    [Test]
    public async Task Npgsql_SomeNin_StillTranslates()
    {
        // `nin` is untouched by the restriction and does translate, via unnest.
        var (sql, errors) = await NpgsqlFilterAsync("badges: { some: { nin: [CHAMPION] } }");

        errors.Should().BeNullOrEmpty();
        sql.Should().Contain("unnest");
    }

    [Test]
    public async Task Npgsql_ComparableSomeGt_StillTranslates()
    {
        var (sql, errors) = await NpgsqlFilterAsync("scores: { some: { gt: 90 } }");

        errors.Should().BeNullOrEmpty();
        sql.Should().NotBeNullOrEmpty();
    }

    [Test]
    public async Task Npgsql_StringSomeContains_StillTranslates()
    {
        var (sql, errors) = await NpgsqlFilterAsync("tags: { some: { contains: \"pro\" } }");

        errors.Should().BeNullOrEmpty();
        sql.Should().NotBeNullOrEmpty();
    }

    [Test]
    public async Task Npgsql_SomeNeq_IsRejectedInsteadOfThrowingAtExecution()
    {
        // The regression this whole change exists for. Without the restriction this
        // compiles to Any(x => x != @p) and Npgsql throws
        // "The LINQ expression ... could not be translated" at execution time.
        var (sql, errors) = await NpgsqlFilterAsync("badges: { some: { neq: CHAMPION } }");

        errors.Should().NotBeNullOrEmpty();
        errors!.Should().Contain(e => e.Message.Contains("neq"));
        // Rejected during validation, so no query was ever compiled.
        sql.Should().BeNull();
        errors.Should().NotContain(e => e.Message.Contains("could not be translated"));
    }

    #endregion

    #region SQL shape — the GIN index declaration decides the operator

    // The difference is invisible above the query plan: same schema, same query, same
    // rows. Only the declared index changes which operator Npgsql compiles, which is what
    // QueryModelScalarCollectionIndexValidator warns about at startup.

    [Test]
    public void SqlShape_WithGinDeclared_MembershipUsesArrayContainment()
    {
        using var db = new NpgsqlShapeDbContext();

        // Badges has HasIndex(...).HasMethod("gin") in the model.
        var sql = db.Rows.Where(r => r.Badges.Contains(Badge.Champion)).ToQueryString();

        sql.Should().Contain("@>");
        sql.Should().NotContain("= ANY");
    }

    [Test]
    public void SqlShape_WithoutGinDeclared_MembershipFallsBackToAny()
    {
        using var db = new NpgsqlShapeDbContext();

        // Tags is the same shape as Badges but carries no index declaration.
        var sql = db.Rows.Where(r => r.Tags.Contains("pro")).ToQueryString();

        sql.Should().Contain("= ANY");
        sql.Should().NotContain("@>");
    }

    [Test]
    public void SqlShape_ListOfT_WithGinDeclared_AlsoUsesArrayContainment()
    {
        // List<T> and T[] both map to a PostgreSQL array and behave identically. The
        // collection type is not what decides the operator.
        using var db = new NpgsqlShapeDbContext();

        var sql = db.Rows.Where(r => r.LegacyTags.Contains("pro")).ToQueryString();

        sql.Should().Contain("@>");
    }

    [Test]
    public void SqlShape_MultiValueOperators_DoNotDependOnTheIndexDeclaration()
    {
        // `some: { in: }` and `all: { in: }` compile to && and <@ either way, so an
        // un-indexed collection filtered only these ways loses nothing.
        using var db = new NpgsqlShapeDbContext();

        var wanted = new[] { "pro" };

        db.Rows.Where(r => r.Tags.Any(t => wanted.Contains(t)))
            .ToQueryString()
            .Should()
            .Contain("&&");

        db.Rows.Where(r => r.Tags.All(t => wanted.Contains(t)))
            .ToQueryString()
            .Should()
            .Contain("<@");
    }

    #endregion

    #region Binder — which properties get a restricted element input

    [Test]
    public void Discover_ScalarCollections_AreBound()
    {
        var bindings = ListElementFilterBinding.Discover([typeof(PlayerRow)]);

        bindings
            .Select(b => b.Key)
            .Should()
            .Contain([typeof(Badge[]), typeof(string[]), typeof(int[]), typeof(List<Badge>)]);
    }

    [Test]
    public void Discover_NavigationCollection_IsNotBound()
    {
        var bindings = ListElementFilterBinding.Discover([typeof(Shelf)]);

        bindings.Should().NotContain(b => b.Key == typeof(List<Book>));
    }

    [Test]
    public void Discover_StringAndByteArray_AreNotBound()
    {
        // string is IEnumerable<char> and byte[] maps to bytea; neither is a scalar array.
        var bindings = ListElementFilterBinding.Discover([typeof(EdgeCaseRow)]);

        bindings.Select(b => b.Key).Should().NotContain([typeof(string), typeof(byte[])]);
    }

    [Test]
    public void Discover_NotMappedProperty_IsNotBound()
    {
        var bindings = ListElementFilterBinding.Discover([typeof(EdgeCaseRow)]);

        // Only the mapped scalar collection is bound; the [NotMapped] one has no column.
        bindings.Should().ContainSingle().Which.Key.Should().Be(typeof(int[]));
    }

    [Test]
    public void Discover_ReadOnlyProperty_IsNotBound()
    {
        var bindings = ListElementFilterBinding.Discover([typeof(ReadOnlyRow)]);

        bindings.Should().BeEmpty();
    }

    [Test]
    public void Discover_SameCollectionTypeAcrossEntities_IsBoundOnce()
    {
        var bindings = ListElementFilterBinding.Discover([typeof(PlayerRow), typeof(OtherRow)]);

        bindings.Count(b => b.Key == typeof(Badge[])).Should().Be(1);
    }

    [Test]
    public void Discover_NoScalarCollections_ReturnsEmpty()
    {
        // Nothing to bind means the wiring keeps HotChocolate's plain AddFiltering().
        var bindings = ListElementFilterBinding.Discover([typeof(Book)]);

        bindings.Should().BeEmpty();
    }

    #endregion

    #region End-to-end through AddTraxGraphQL

    [Test]
    public async Task AddTraxGraphQL_AppliesRestrictionThroughRealWiring()
    {
        // Exercises GraphQLServiceExtensions rather than the hand-rolled harness.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TraxMarker>();
        services.AddSingleton(Substitute.For<ITrainDiscoveryService>());
        services.AddSingleton(Substitute.For<IEffectRegistry>());
        services.AddSingleton(Substitute.For<ITraxScheduler>());
        services.AddSingleton(Substitute.For<ITraxHealthService>());
        services.AddDbContext<PlayersDbContext>(o =>
            o.UseInMemoryDatabase("ListElemWiring_" + Guid.NewGuid(), new InMemoryDatabaseRoot())
        );

        services.AddTraxGraphQL(g => g.AddDbContext<PlayersDbContext>());

        await using var provider = services.BuildServiceProvider();
        var executor = await provider
            .GetRequiredService<IRequestExecutorProvider>()
            .GetExecutorAsync("trax");

        var element = executor.Schema.Types.GetType<InputObjectType>("BadgeElementFilterInput");
        element.Fields.Select(f => f.Name).Should().NotContain("neq");

        var scalar = executor.Schema.Types.GetType<InputObjectType>("BadgeOperationFilterInput");
        scalar.Fields.Select(f => f.Name).Should().Contain("neq");
    }

    [Test]
    public async Task ConfigureFiltering_ComposesWithTheRestriction()
    {
        // The opt-in module path and the automatic binding share one AddFiltering call;
        // both must land.
        var executor = await BuildExecutorAsync(
            seedData: true,
            customize: b => b.ConfigureFiltering(f => f.AddCaseInsensitiveStringOperations())
        );

        executor
            .Schema.Types.GetType<InputObjectType>("StringOperationFilterInput")
            .Fields.Select(f => f.Name)
            .Should()
            .Contain("icontains");

        executor
            .Schema.Types.GetType<InputObjectType>("BadgeElementFilterInput")
            .Fields.Select(f => f.Name)
            .Should()
            .NotContain("neq");
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Runs one filter through a real HotChocolate pipeline backed by the Npgsql
    /// provider and returns the SQL it compiled, or the GraphQL errors if the query never
    /// got that far. A fresh executor per call keeps the captured SQL local to the call.
    /// </summary>
    private static async Task<(string? Sql, IReadOnlyList<IError>? Errors)> NpgsqlFilterAsync(
        string where
    )
    {
        string? captured = null;

        var services = new ServiceCollection();
        services.AddDbContext<NpgsqlShapeDbContext>();

        var bindings = ListElementFilterBinding.Discover([typeof(ShapeRow)]);

        services
            .AddGraphQL()
            .AddQueryType(
                new ObjectType(descriptor =>
                {
                    descriptor.Name("Query");
                    descriptor
                        .Field("rows")
                        .Type<ListType<ObjectType<ShapeRow>>>()
                        // Registered outside UseFiltering so it observes the filtered
                        // IQueryable on the way back out.
                        .Use(next =>
                            async context =>
                            {
                                await next(context);
                                if (context.Result is IQueryable<ShapeRow> queryable)
                                {
                                    captured = queryable.ToQueryString();
                                    context.Result = Array.Empty<ShapeRow>();
                                }
                            }
                        )
                        .UseFiltering<ShapeRow>()
                        .Resolve(context =>
                            context.Service<NpgsqlShapeDbContext>().Rows.AsQueryable()
                        );
                })
            )
            .AddFiltering(convention =>
            {
                convention.AddDefaults();
                ListElementFilterBinding.Apply(convention, bindings);
            });

        await using var provider = services.BuildServiceProvider();
        var executor = await provider
            .GetRequiredService<IRequestExecutorProvider>()
            .GetExecutorAsync();

        var result = await executor.ExecuteAsync($"{{ rows(where: {{ {where} }}) {{ id }} }}");
        var op = result.ExpectOperationResult();

        return (captured, op.Errors);
    }

    private static async Task<List<int>> QueryIdsAsync(IRequestExecutor executor, string where)
    {
        var result = await executor.ExecuteAsync(
            $"{{ discover {{ players(where: {{ {where} }}) {{ nodes {{ id }} }} }} }}"
        );

        var op = result.ExpectOperationResult();
        op.Errors.Should()
            .BeNullOrEmpty(
                "the query must execute, but got: {0}",
                string.Join(" | ", op.Errors?.Select(e => e.Message) ?? [])
            );

        var discover = (IReadOnlyDictionary<string, object?>)op.DataMap()["discover"]!;
        var players = (IReadOnlyDictionary<string, object?>)discover["players"]!;
        var nodes = (IReadOnlyList<object?>)players["nodes"]!;

        return nodes
            .Select(n => Convert.ToInt32(((IReadOnlyDictionary<string, object?>)n!)["id"]))
            .OrderBy(id => id)
            .ToList();
    }

    private static async Task<ISchemaDefinition> BuildSchemaAsync<TContext>()
        where TContext : DbContext => (await BuildExecutorAsync<TContext>()).Schema;

    private static Task<ISchemaDefinition> BuildSchemaAsync() =>
        BuildSchemaAsync<PlayersDbContext>();

    private static Task<IRequestExecutor> BuildExecutorAsync(
        bool seedData = false,
        Action<TraxGraphQLBuilder>? customize = null
    ) => BuildExecutorAsync<PlayersDbContext>(seedData, customize);

    private static async Task<IRequestExecutor> BuildExecutorAsync<TContext>(
        bool seedData = false,
        Action<TraxGraphQLBuilder>? customize = null
    )
        where TContext : DbContext
    {
        var dbName = "ListElemTest_" + Guid.NewGuid();
        var dbRoot = new InMemoryDatabaseRoot();

        var services = new ServiceCollection();
        services.AddDbContext<TContext>(o => o.UseInMemoryDatabase(dbName, dbRoot));

        var builder = new TraxGraphQLBuilder(services);
        builder.AddDbContext<TContext>();
        customize?.Invoke(builder);
        var config = builder.Build();

        services.AddSingleton(config);
        services.AddSingleton<QueryModelTypeModule>();

        var bindings = ListElementFilterBinding.Discover(
            config.ModelRegistrations.Select(r => r.EntityType)
        );

        var graphql = services
            .AddGraphQLServer()
            .AddQueryType<ListElemRootQuery>()
            .AddType<ListElemDiscoverQueriesType>()
            .AddTypeModule<QueryModelTypeModule>()
            .AddSorting()
            .AddProjections();

        // Mirror GraphQLServiceExtensions: bindings and opt-in modules share one call.
        graphql.AddFiltering(convention =>
        {
            convention.AddDefaults();
            ListElementFilterBinding.Apply(convention, bindings);
            foreach (var module in config.FilterModules)
                module.Apply(convention);
        });

        var provider = services.BuildServiceProvider();

        if (seedData)
            await SeedAsync<TContext>(provider);

        return await provider.GetRequiredService<IRequestExecutorProvider>().GetExecutorAsync();
    }

    private static async Task SeedAsync<TContext>(IServiceProvider provider)
        where TContext : DbContext
    {
        if (provider.GetRequiredService<TContext>() is not PlayersDbContext players)
            return;

        players.Players.AddRange(
            new PlayerRow
            {
                Id = 1,
                Tier = Badge.Founder,
                Badges = [Badge.Founder, Badge.Champion],
                Tags = ["pro", "eu"],
                Scores = [10, 20],
            },
            new PlayerRow
            {
                Id = 2,
                Tier = Badge.Veteran,
                Badges = [Badge.Veteran],
                Tags = ["amateur"],
                Scores = [95],
            },
            new PlayerRow
            {
                Id = 3,
                Tier = Badge.Champion,
                Badges = [Badge.Champion],
                Tags = ["eu"],
                Scores = [50],
            },
            new PlayerRow
            {
                Id = 4,
                Tier = Badge.Veteran,
                Badges = [],
                Tags = [],
                Scores = [],
            }
        );

        await players.SaveChangesAsync();
    }

    public class ListElemRootQuery
    {
        public DiscoverQueries Discover() => new();
    }

    public class ListElemDiscoverQueriesType : ObjectType<DiscoverQueries>
    {
        protected override void Configure(IObjectTypeDescriptor<DiscoverQueries> descriptor) =>
            descriptor.Name("DiscoverQueries");
    }

    #endregion
}

#region Test entities and contexts

public enum Badge
{
    Founder,
    Veteran,
    Champion,
}

[TraxAllowAnonymous]
[TraxQueryModel(Name = "players")]
public class PlayerRow
{
    public int Id { get; set; }

    /// <summary>Scalar enum: keeps the full operation set, `neq` included.</summary>
    public Badge Tier { get; set; }

    /// <summary>Scalar string: same, and gives the schema a StringOperationFilterInput.</summary>
    public string Name { get; set; } = "";

    public Badge[] Badges { get; set; } = [];

    public string[] Tags { get; set; } = [];

    public int[] Scores { get; set; } = [];

    /// <summary>The List&lt;T&gt; shape carries the same trap and is restricted too.</summary>
    public List<Badge> LegacyBadges { get; set; } = [];
}

[TraxAllowAnonymous]
[TraxQueryModel(Name = "others")]
public class OtherRow
{
    public int Id { get; set; }
    public Badge[] Badges { get; set; } = [];
}

[TraxAllowAnonymous]
[TraxQueryModel(Name = "edgeCases")]
public class EdgeCaseRow
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public byte[] Blob { get; set; } = [];
    public int[] Mapped { get; set; } = [];

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public int[] Unmapped { get; set; } = [];
}

[TraxAllowAnonymous]
[TraxQueryModel(Name = "readOnlyRows")]
public class ReadOnlyRow
{
    public int Id { get; set; }
    public int[] Computed { get; } = [];
}

[TraxAllowAnonymous]
[TraxQueryModel(Name = "books")]
public class Book
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
}

[TraxAllowAnonymous]
[TraxQueryModel(Name = "shelves")]
public class Shelf
{
    public int Id { get; set; }
    public List<Book> Books { get; set; } = [];
}

[TraxAllowAnonymous]
[TraxQueryModel(Name = "allScalarKinds")]
public class AllScalarKindsRow
{
    public int Id { get; set; }

    public short[] Shorts { get; set; } = [];
    public int[] Scores { get; set; } = [];
    public int?[] OptionalScores { get; set; } = [];
    public long[] Counts { get; set; } = [];
    public List<byte> Bytes { get; set; } = [];
    public float[] Floats { get; set; } = [];
    public double[] Doubles { get; set; } = [];
    public decimal[] Amounts { get; set; } = [];
    public bool[] Flags { get; set; } = [];
    public string[] Tags { get; set; } = [];
    public Guid[] Keys { get; set; } = [];
    public DateTime[] Stamps { get; set; } = [];
    public DateTimeOffset[] Offsets { get; set; } = [];
    public Badge[] Badges { get; set; } = [];
}

public class AllScalarKindsDbContext(DbContextOptions<AllScalarKindsDbContext> options)
    : DbContext(options)
{
    public DbSet<AllScalarKindsRow> Rows => Set<AllScalarKindsRow>();
}

public class PlayersDbContext(DbContextOptions<PlayersDbContext> options) : DbContext(options)
{
    public DbSet<PlayerRow> Players => Set<PlayerRow>();
}

public class LibraryDbContext(DbContextOptions<LibraryDbContext> options) : DbContext(options)
{
    public DbSet<Shelf> Shelves => Set<Shelf>();
    public DbSet<Book> Books => Set<Book>();
}

/// <summary>
/// A real Npgsql model used only to compile queries and read back the SQL. It never
/// opens a connection, so it needs no database.
/// </summary>
public class NpgsqlShapeDbContext : DbContext
{
    public DbSet<ShapeRow> Rows => Set<ShapeRow>();

    protected override void OnConfiguring(DbContextOptionsBuilder options) =>
        options.UseNpgsql(
            "Host=localhost;Database=shape;Username=shape;Password=shape",
            npgsql => npgsql.MapEnum<Badge>("badge")
        );

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresEnum<Badge>();
        // Declared on Badges (an array) and LegacyTags (a List<T>) but deliberately not
        // on Tags or Scores, so the tests can compare the two translations.
        modelBuilder.Entity<ShapeRow>().HasIndex(r => r.Badges).HasMethod("gin");
        modelBuilder.Entity<ShapeRow>().HasIndex(r => r.LegacyTags).HasMethod("gin");
    }
}

public class ShapeRow
{
    public int Id { get; set; }
    public Badge[] Badges { get; set; } = [];
    public string[] Tags { get; set; } = [];
    public int[] Scores { get; set; } = [];
    public List<string> LegacyTags { get; set; } = [];
}

#endregion
