using System.ComponentModel.DataAnnotations.Schema;
using FluentAssertions;
using HotChocolate.Types;
using Microsoft.EntityFrameworkCore;
using Trax.Api.GraphQL.Configuration;
using Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;
using Trax.Api.GraphQL.Queries;
using Trax.Api.GraphQL.TypeModules;
using Trax.Effect.Attributes;

namespace Trax.Api.Tests;

[TestFixture]
public class QueryModelTypeModuleTests
{
    #region DeriveModelName — Pluralization

    [Test]
    public void DeriveModelName_SimpleClass_PluralizesCorrectly()
    {
        QueryModelTypeModule.DeriveModelName("Player").Should().Be("players");
    }

    [Test]
    public void DeriveModelName_EndsWithS_AddsEs()
    {
        QueryModelTypeModule.DeriveModelName("Address").Should().Be("addresses");
    }

    [Test]
    public void DeriveModelName_EndsWithX_AddsEs()
    {
        QueryModelTypeModule.DeriveModelName("Box").Should().Be("boxes");
    }

    [Test]
    public void DeriveModelName_EndsWithZ_AddsEs()
    {
        QueryModelTypeModule.DeriveModelName("Quiz").Should().Be("quizes");
    }

    [Test]
    public void DeriveModelName_EndsWithCh_AddsEs()
    {
        QueryModelTypeModule.DeriveModelName("Match").Should().Be("matches");
    }

    [Test]
    public void DeriveModelName_EndsWithSh_AddsEs()
    {
        QueryModelTypeModule.DeriveModelName("Crash").Should().Be("crashes");
    }

    [Test]
    public void DeriveModelName_EndsWithConsonantY_ChangesToIes()
    {
        QueryModelTypeModule.DeriveModelName("Category").Should().Be("categories");
    }

    [Test]
    public void DeriveModelName_EndsWithVowelY_AddsS()
    {
        QueryModelTypeModule.DeriveModelName("Key").Should().Be("keys");
    }

    [Test]
    public void DeriveModelName_PascalCase_CamelCasesResult()
    {
        QueryModelTypeModule.DeriveModelName("MatchResult").Should().Be("matchResults");
    }

    [Test]
    public void DeriveModelName_SingleChar_PluralizesCorrectly()
    {
        QueryModelTypeModule.DeriveModelName("A").Should().Be("as");
    }

    #endregion

    #region Pluralize Edge Cases

    [Test]
    public void Pluralize_EndsWithS_AddsEs()
    {
        QueryModelTypeModule.Pluralize("Bus").Should().Be("Buses");
    }

    [Test]
    public void Pluralize_SimpleWord_AddsS()
    {
        QueryModelTypeModule.Pluralize("Player").Should().Be("Players");
    }

    [Test]
    public void Pluralize_EndsWithAy_AddsS()
    {
        QueryModelTypeModule.Pluralize("Day").Should().Be("Days");
    }

    #endregion

    #region Builder — DbContext Scanning

    [Test]
    public void Build_NoDbContexts_ReturnsEmptyRegistrations()
    {
        var builder = new TraxGraphQLBuilder(
            new Microsoft.Extensions.DependencyInjection.ServiceCollection()
        );
        var config = builder.Build();

        config.ModelRegistrations.Should().BeEmpty();
    }

    [Test]
    public void Build_DbContextWithAttributedEntities_DiscoversCorrectly()
    {
        var builder = new TraxGraphQLBuilder(
            new Microsoft.Extensions.DependencyInjection.ServiceCollection()
        );
        builder.AddDbContext<TestDbContext>();
        var config = builder.Build();

        config.ModelRegistrations.Should().HaveCount(1);
        config.ModelRegistrations[0].EntityType.Should().Be(typeof(TestPlayer));
        config.ModelRegistrations[0].DbContextType.Should().Be(typeof(TestDbContext));
        config.ModelRegistrations[0].Attribute.Description.Should().Be("Test players");
    }

    [Test]
    public void Build_DbContextWithMixedEntities_OnlyDiscoversAttributed()
    {
        var builder = new TraxGraphQLBuilder(
            new Microsoft.Extensions.DependencyInjection.ServiceCollection()
        );
        builder.AddDbContext<MixedDbContext>();
        var config = builder.Build();

        config.ModelRegistrations.Should().HaveCount(1);
        config.ModelRegistrations[0].EntityType.Should().Be(typeof(TestPlayer));
    }

    [Test]
    public void Build_AttributeWithFeatureToggles_PreservesSettings()
    {
        var builder = new TraxGraphQLBuilder(
            new Microsoft.Extensions.DependencyInjection.ServiceCollection()
        );
        builder.AddDbContext<ToggleDbContext>();
        var config = builder.Build();

        config.ModelRegistrations.Should().HaveCount(1);
        var attr = config.ModelRegistrations[0].Attribute;
        attr.Paging.Should().BeTrue();
        attr.Filtering.Should().BeFalse();
        attr.Sorting.Should().BeFalse();
        attr.Projection.Should().BeTrue();
    }

    [Test]
    public void Build_AttributeDefaults_AllFeaturesEnabled()
    {
        var builder = new TraxGraphQLBuilder(
            new Microsoft.Extensions.DependencyInjection.ServiceCollection()
        );
        builder.AddDbContext<TestDbContext>();
        var config = builder.Build();

        var attr = config.ModelRegistrations[0].Attribute;
        attr.Paging.Should().BeTrue();
        attr.Filtering.Should().BeTrue();
        attr.Sorting.Should().BeTrue();
        attr.Projection.Should().BeTrue();
    }

    [Test]
    public void Build_MultipleDbContexts_DiscoversFromAll()
    {
        var builder = new TraxGraphQLBuilder(
            new Microsoft.Extensions.DependencyInjection.ServiceCollection()
        );
        builder.AddDbContext<TestDbContext>();
        builder.AddDbContext<SecondDbContext>();
        var config = builder.Build();

        config.ModelRegistrations.Should().HaveCount(2);
        config
            .ModelRegistrations.Select(r => r.EntityType)
            .Should()
            .Contain([typeof(TestPlayer), typeof(TestItem)]);
    }

    #endregion

    #region CreateTypesAsync — Type Generation

    [Test]
    public async Task CreateTypesAsync_NoRegistrations_ReturnsEmpty()
    {
        var config = new GraphQLConfiguration([], [], []);
        var module = new QueryModelTypeModule(config);

        var types = await module.CreateTypesAsync(null!, CancellationToken.None);

        types.Should().BeEmpty();
    }

    [Test]
    public async Task CreateTypesAsync_WithRegistrations_CreatesObjectTypeAndExtension()
    {
        var config = new GraphQLConfiguration(
            [
                new QueryModelRegistration(
                    typeof(TestPlayer),
                    typeof(TestDbContext),
                    new TraxQueryModelAttribute { Description = "Test players" }
                ),
            ],
            [],
            []
        );
        var module = new QueryModelTypeModule(config);

        var types = await module.CreateTypesAsync(null!, CancellationToken.None);

        // ObjectType<TestPlayer> + ObjectTypeExtension on DiscoverQueries
        types.Should().HaveCount(2);

        types
            .Should()
            .ContainSingle(t =>
                t.GetType().IsGenericType
                && t.GetType().GetGenericTypeDefinition() == typeof(ObjectType<>)
                && t.GetType().GetGenericArguments()[0] == typeof(TestPlayer)
            );

        types.OfType<ObjectTypeExtension>().Should().HaveCount(1);
    }

    [Test]
    public async Task CreateTypesAsync_DuplicateEntityTypes_RegistersTypeOnce()
    {
        var config = new GraphQLConfiguration(
            [
                new QueryModelRegistration(
                    typeof(TestPlayer),
                    typeof(TestDbContext),
                    new TraxQueryModelAttribute { Name = "players1" }
                ),
                new QueryModelRegistration(
                    typeof(TestPlayer),
                    typeof(TestDbContext),
                    new TraxQueryModelAttribute { Name = "players2" }
                ),
            ],
            [],
            []
        );
        var module = new QueryModelTypeModule(config);

        var types = await module.CreateTypesAsync(null!, CancellationToken.None);

        // Only 1 ObjectType<TestPlayer> despite 2 registrations
        types
            .Where(t =>
                t.GetType().IsGenericType
                && t.GetType().GetGenericTypeDefinition() == typeof(ObjectType<>)
            )
            .Should()
            .HaveCount(1);
    }

    #endregion

    #region TrainTypeModule Coordination

    [Test]
    public async Task TrainTypeModule_WithModelRegistrations_SkipsDiscoverQueriesBaseType()
    {
        var discovery = new StubDiscoveryService([
            new Trax.Mediator.Services.TrainDiscovery.TrainRegistration
            {
                ServiceType = typeof(IStubTrain),
                ImplementationType = typeof(StubTrain),
                InputType = typeof(StubInput),
                OutputType = typeof(StubOutput),
                Lifetime = Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped,
                ServiceTypeName = "LookupTrain",
                ImplementationTypeName = "LookupTrain",
                InputTypeName = nameof(StubInput),
                OutputTypeName = nameof(StubOutput),
                RequiredPolicies = [],
                RequiredRoles = [],
                IsQuery = true,
                IsMutation = false,
                IsBroadcastEnabled = false,
                GraphQLName = "Lookup",
                GraphQLOperations = Trax.Effect.Attributes.GraphQLOperation.Run,
                IsRemote = false,
            },
        ]);

        var config = new GraphQLConfiguration(
            [
                new QueryModelRegistration(
                    typeof(TestPlayer),
                    typeof(TestDbContext),
                    new TraxQueryModelAttribute()
                ),
            ],
            [],
            []
        );

        var module = new Trax.Api.GraphQL.TypeModules.TrainTypeModule(discovery, config);
        var types = await module.CreateTypesAsync(null!, CancellationToken.None);

        // Should NOT contain ObjectType<DiscoverQueries> (base type registered externally)
        types
            .Should()
            .NotContain(t =>
                t.GetType().IsGenericType
                && t.GetType().GetGenericTypeDefinition() == typeof(ObjectType<>)
                && t.GetType().GetGenericArguments()[0] == typeof(DiscoverQueries)
            );

        // Should still have the field extension on DiscoverQueries
        types.OfType<ObjectTypeExtension>().Should().HaveCount(1);
    }

    [Test]
    public async Task TrainTypeModule_WithoutModelRegistrations_CreatesDiscoverQueriesBaseType()
    {
        var discovery = new StubDiscoveryService([
            new Trax.Mediator.Services.TrainDiscovery.TrainRegistration
            {
                ServiceType = typeof(IStubTrain),
                ImplementationType = typeof(StubTrain),
                InputType = typeof(StubInput),
                OutputType = typeof(StubOutput),
                Lifetime = Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped,
                ServiceTypeName = "LookupTrain",
                ImplementationTypeName = "LookupTrain",
                InputTypeName = nameof(StubInput),
                OutputTypeName = nameof(StubOutput),
                RequiredPolicies = [],
                RequiredRoles = [],
                IsQuery = true,
                IsMutation = false,
                IsBroadcastEnabled = false,
                GraphQLName = "Lookup",
                GraphQLOperations = Trax.Effect.Attributes.GraphQLOperation.Run,
                IsRemote = false,
            },
        ]);

        var module = new Trax.Api.GraphQL.TypeModules.TrainTypeModule(discovery);
        var types = await module.CreateTypesAsync(null!, CancellationToken.None);

        // SHOULD contain ObjectType<DiscoverQueries> (no model registrations, so we own it)
        types
            .Should()
            .ContainSingle(t =>
                t.GetType().IsGenericType
                && t.GetType().GetGenericTypeDefinition() == typeof(ObjectType<>)
                && t.GetType().GetGenericArguments()[0] == typeof(DiscoverQueries)
            );

        // DiscoverQueries field extension + RootQuery discover extension
        types.OfType<ObjectTypeExtension>().Should().HaveCount(2);
    }

    #endregion

    #region Attribute Property Tests

    [Test]
    public void TraxQueryModelAttribute_DefaultProperties_FeaturesAllTrue()
    {
        var attr = new TraxQueryModelAttribute();
        attr.Name.Should().BeNull();
        attr.Description.Should().BeNull();
        attr.DeprecationReason.Should().BeNull();
        attr.Paging.Should().BeTrue();
        attr.Filtering.Should().BeTrue();
        attr.Sorting.Should().BeTrue();
        attr.Projection.Should().BeTrue();
        attr.BindFields.Should().Be(FieldBindingBehavior.Implicit);
    }

    [Test]
    public void TraxQueryModelAttribute_WithInitProperties_SetsCorrectly()
    {
        var attr = new TraxQueryModelAttribute
        {
            Name = "allPlayers",
            Description = "All players",
            DeprecationReason = "Use v2",
            Paging = false,
            Filtering = false,
            Sorting = true,
            Projection = false,
        };

        attr.Name.Should().Be("allPlayers");
        attr.Description.Should().Be("All players");
        attr.DeprecationReason.Should().Be("Use v2");
        attr.Paging.Should().BeFalse();
        attr.Filtering.Should().BeFalse();
        attr.Sorting.Should().BeTrue();
        attr.Projection.Should().BeFalse();
    }

    #endregion

    #region Namespace Grouping

    [Test]
    public async Task CreateTypesAsync_ModelWithNamespace_CreatesNamespaceTypeAndExtension()
    {
        var config = new GraphQLConfiguration(
            [
                new QueryModelRegistration(
                    typeof(TestPlayer),
                    typeof(TestDbContext),
                    new TraxQueryModelAttribute { Description = "Test players", Namespace = "game" }
                ),
            ],
            [],
            []
        );
        var module = new QueryModelTypeModule(config);

        var types = await module.CreateTypesAsync(null!, CancellationToken.None);

        // ObjectType<TestPlayer> + ObjectType (namespace base) + 2x ObjectTypeExtension
        // (namespace fields + namespace field on DiscoverQueries)
        types.OfType<ObjectTypeExtension>().Should().HaveCount(2);

        // Namespace type should be tracked
        config.RegisteredNamespaceTypes.Should().Contain("GameDiscoverQueries");
        config.RegisteredNamespaceTypes.Should().Contain("DiscoverQueries.game");
    }

    [Test]
    public async Task CreateTypesAsync_ModelWithoutNamespace_NoNamespaceTypesCreated()
    {
        var config = new GraphQLConfiguration(
            [
                new QueryModelRegistration(
                    typeof(TestPlayer),
                    typeof(TestDbContext),
                    new TraxQueryModelAttribute { Description = "Test players" }
                ),
            ],
            [],
            []
        );
        var module = new QueryModelTypeModule(config);

        var types = await module.CreateTypesAsync(null!, CancellationToken.None);

        // Should still have one extension directly on DiscoverQueries
        types.OfType<ObjectTypeExtension>().Should().HaveCount(1);
        config.RegisteredNamespaceTypes.Should().BeEmpty();
    }

    [Test]
    public void TraxQueryModelAttribute_Namespace_DefaultsToNull()
    {
        var attr = new TraxQueryModelAttribute();
        attr.Namespace.Should().BeNull();
    }

    [Test]
    public void TraxQueryModelAttribute_Namespace_SetsCorrectly()
    {
        var attr = new TraxQueryModelAttribute { Namespace = "game" };
        attr.Namespace.Should().Be("game");
    }

    [Test]
    public async Task CreateTypesAsync_MixedNamespacedAndRootModels_BothGenerated()
    {
        var config = new GraphQLConfiguration(
            [
                new QueryModelRegistration(
                    typeof(TestPlayer),
                    typeof(TestDbContext),
                    new TraxQueryModelAttribute { Namespace = "game" }
                ),
                new QueryModelRegistration(
                    typeof(TestItem),
                    typeof(SecondDbContext),
                    new TraxQueryModelAttribute()
                ),
            ],
            [],
            []
        );
        var module = new QueryModelTypeModule(config);

        var types = await module.CreateTypesAsync(null!, CancellationToken.None);

        // Root model extension on DiscoverQueries + namespace field extension on DiscoverQueries
        // + namespace type extension (GameDiscoverQueries)
        types.OfType<ObjectTypeExtension>().Should().HaveCount(3);
        config.RegisteredNamespaceTypes.Should().Contain("GameDiscoverQueries");
    }

    #endregion

    #region Explicit Field Binding

    [Test]
    public void TraxQueryModelAttribute_BindFields_SetsToExplicit()
    {
        var attr = new TraxQueryModelAttribute { BindFields = FieldBindingBehavior.Explicit };
        attr.BindFields.Should().Be(FieldBindingBehavior.Explicit);
    }

    [Test]
    public void Build_ExplicitBindingAttribute_PreservesBindFieldsSetting()
    {
        var builder = new TraxGraphQLBuilder(
            new Microsoft.Extensions.DependencyInjection.ServiceCollection()
        );
        builder.AddDbContext<ExplicitDbContext>();
        var config = builder.Build();

        config.ModelRegistrations.Should().HaveCount(1);
        config
            .ModelRegistrations[0]
            .Attribute.BindFields.Should()
            .Be(FieldBindingBehavior.Explicit);
    }

    [Test]
    public async Task CreateTypesAsync_ExplicitBinding_CreatesObjectTypeAndExtension()
    {
        var config = new GraphQLConfiguration(
            [
                new QueryModelRegistration(
                    typeof(ExplicitEntity),
                    typeof(ExplicitDbContext),
                    new TraxQueryModelAttribute { BindFields = FieldBindingBehavior.Explicit }
                ),
            ],
            [],
            []
        );
        var module = new QueryModelTypeModule(config);

        var types = await module.CreateTypesAsync(null!, CancellationToken.None);

        types.Should().HaveCount(2);
        types
            .Should()
            .ContainSingle(t =>
                t.GetType().IsGenericType
                && t.GetType().GetGenericTypeDefinition() == typeof(ObjectType<>)
                && t.GetType().GetGenericArguments()[0] == typeof(ExplicitEntity)
            );
        types.OfType<ObjectTypeExtension>().Should().HaveCount(1);
    }

    [Test]
    public async Task CreateTypesAsync_ImplicitBinding_CreatesDefaultObjectType()
    {
        var config = new GraphQLConfiguration(
            [
                new QueryModelRegistration(
                    typeof(TestPlayer),
                    typeof(TestDbContext),
                    new TraxQueryModelAttribute()
                ),
            ],
            [],
            []
        );
        var module = new QueryModelTypeModule(config);

        var types = await module.CreateTypesAsync(null!, CancellationToken.None);

        types.Should().HaveCount(2);
        types
            .Should()
            .ContainSingle(t =>
                t.GetType().IsGenericType
                && t.GetType().GetGenericTypeDefinition() == typeof(ObjectType<>)
                && t.GetType().GetGenericArguments()[0] == typeof(TestPlayer)
            );
    }

    #endregion

    #region Stubs

    private class StubDiscoveryService(
        IReadOnlyList<Trax.Mediator.Services.TrainDiscovery.TrainRegistration> registrations
    ) : Trax.Mediator.Services.TrainDiscovery.ITrainDiscoveryService
    {
        public IReadOnlyList<Trax.Mediator.Services.TrainDiscovery.TrainRegistration> DiscoverTrains() =>
            registrations;
    }

    private interface IStubTrain;

    private class StubTrain;

    #endregion
}

#region Test Entities and DbContexts

[TraxQueryModel(Description = "Test players")]
public class TestPlayer
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public class TestIgnored
{
    public int Id { get; set; }
    public string Value { get; set; } = "";
}

[TraxQueryModel(Filtering = false, Sorting = false)]
public class ToggleEntity
{
    public int Id { get; set; }
}

[TraxQueryModel(Description = "Test items")]
public class TestItem
{
    public int Id { get; set; }
    public string ItemName { get; set; } = "";
}

public class TestDbContext : DbContext
{
    public DbSet<TestPlayer> Players { get; set; } = null!;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseInMemoryDatabase("TestDb_" + Guid.NewGuid());
}

public class MixedDbContext : DbContext
{
    public DbSet<TestPlayer> Players { get; set; } = null!;
    public DbSet<TestIgnored> Ignored { get; set; } = null!;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseInMemoryDatabase("MixedDb_" + Guid.NewGuid());
}

public class ToggleDbContext : DbContext
{
    public DbSet<ToggleEntity> Toggles { get; set; } = null!;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseInMemoryDatabase("ToggleDb_" + Guid.NewGuid());
}

public class SecondDbContext : DbContext
{
    public DbSet<TestItem> Items { get; set; } = null!;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseInMemoryDatabase("SecondDb_" + Guid.NewGuid());
}

public record StubInput
{
    public string Value { get; init; } = "";
}

public record StubOutput
{
    public string Result { get; init; } = "";
}

[TraxQueryModel(BindFields = FieldBindingBehavior.Explicit)]
public class ExplicitEntity
{
    [Column("id")]
    public int Id { get; set; }

    [Column("display_name")]
    public string DisplayName { get; set; } = "";

    [NotMapped]
    public string ComputedAlias => $"Entity-{Id}";

    public string NonColumnProp { get; set; } = "";

    public void AddToDbContext() { }
}

public class ExplicitDbContext : DbContext
{
    public DbSet<ExplicitEntity> Entities { get; set; } = null!;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseInMemoryDatabase("ExplicitDb_" + Guid.NewGuid());
}

#endregion
