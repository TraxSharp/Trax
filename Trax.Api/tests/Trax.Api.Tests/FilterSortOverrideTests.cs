using FluentAssertions;
using HotChocolate.Data.Filters;
using HotChocolate.Data.Sorting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.Configuration;
using Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;
using Trax.Effect.Attributes;

namespace Trax.Api.Tests;

[TestFixture]
public class FilterSortOverrideTests
{
    #region AddFilterType — Builder Storage

    [Test]
    public void AddFilterType_StoresOverride()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());

        builder.AddFilterType<TestPlayer, TestPlayerFilterInputType>();

        builder.FilterTypeOverrides.Should().ContainKey(typeof(TestPlayer));
        builder
            .FilterTypeOverrides[typeof(TestPlayer)]
            .Should()
            .Be(typeof(TestPlayerFilterInputType));
    }

    [Test]
    public void AddFilterType_MultipleDifferentEntities_AllStored()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());

        builder.AddFilterType<TestPlayer, TestPlayerFilterInputType>();
        builder.AddFilterType<TestItem, TestItemFilterInputType>();

        builder.FilterTypeOverrides.Should().HaveCount(2);
        builder
            .FilterTypeOverrides[typeof(TestPlayer)]
            .Should()
            .Be(typeof(TestPlayerFilterInputType));
        builder.FilterTypeOverrides[typeof(TestItem)].Should().Be(typeof(TestItemFilterInputType));
    }

    [Test]
    public void AddFilterType_ReturnsSameBuilder()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());

        var result = builder.AddFilterType<TestPlayer, TestPlayerFilterInputType>();

        result.Should().BeSameAs(builder);
    }

    [Test]
    public void AddFilterType_SameEntityTwice_LastWins()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());

        builder.AddFilterType<TestPlayer, TestPlayerFilterInputType>();
        builder.AddFilterType<TestPlayer, TestPlayerFilterInputTypeAlternate>();

        builder
            .FilterTypeOverrides[typeof(TestPlayer)]
            .Should()
            .Be(typeof(TestPlayerFilterInputTypeAlternate));
    }

    #endregion

    #region AddSortType — Builder Storage

    [Test]
    public void AddSortType_StoresOverride()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());

        builder.AddSortType<TestPlayer, TestPlayerSortInputType>();

        builder.SortTypeOverrides.Should().ContainKey(typeof(TestPlayer));
        builder.SortTypeOverrides[typeof(TestPlayer)].Should().Be(typeof(TestPlayerSortInputType));
    }

    [Test]
    public void AddSortType_MultipleDifferentEntities_AllStored()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());

        builder.AddSortType<TestPlayer, TestPlayerSortInputType>();
        builder.AddSortType<TestItem, TestItemSortInputType>();

        builder.SortTypeOverrides.Should().HaveCount(2);
        builder.SortTypeOverrides[typeof(TestPlayer)].Should().Be(typeof(TestPlayerSortInputType));
        builder.SortTypeOverrides[typeof(TestItem)].Should().Be(typeof(TestItemSortInputType));
    }

    [Test]
    public void AddSortType_ReturnsSameBuilder()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());

        var result = builder.AddSortType<TestPlayer, TestPlayerSortInputType>();

        result.Should().BeSameAs(builder);
    }

    #endregion

    #region AddFilterType + AddSortType Combined

    [Test]
    public void AddFilterType_AndSortType_SameEntity_BothStored()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());

        builder
            .AddFilterType<TestPlayer, TestPlayerFilterInputType>()
            .AddSortType<TestPlayer, TestPlayerSortInputType>();

        builder.FilterTypeOverrides.Should().ContainKey(typeof(TestPlayer));
        builder.SortTypeOverrides.Should().ContainKey(typeof(TestPlayer));
    }

    [Test]
    public void AddFilterType_WithDbContext_FluentChaining()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());

        builder
            .AddDbContext<TestDbContext>()
            .AddFilterType<TestPlayer, TestPlayerFilterInputType>()
            .AddSortType<TestPlayer, TestPlayerSortInputType>();

        builder.DbContextTypes.Should().HaveCount(1);
        builder.FilterTypeOverrides.Should().HaveCount(1);
        builder.SortTypeOverrides.Should().HaveCount(1);
    }

    #endregion

    #region Build — Propagation to QueryModelRegistration

    [Test]
    public void Build_WithFilterOverride_PopulatesRegistration()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());
        builder.AddDbContext<TestDbContext>();
        builder.AddFilterType<TestPlayer, TestPlayerFilterInputType>();

        var config = builder.Build();

        config.ModelRegistrations.Should().HaveCount(1);
        config.ModelRegistrations[0].FilterInputType.Should().Be(typeof(TestPlayerFilterInputType));
        config.ModelRegistrations[0].SortInputType.Should().BeNull();
    }

    [Test]
    public void Build_WithSortOverride_PopulatesRegistration()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());
        builder.AddDbContext<TestDbContext>();
        builder.AddSortType<TestPlayer, TestPlayerSortInputType>();

        var config = builder.Build();

        config.ModelRegistrations.Should().HaveCount(1);
        config.ModelRegistrations[0].SortInputType.Should().Be(typeof(TestPlayerSortInputType));
        config.ModelRegistrations[0].FilterInputType.Should().BeNull();
    }

    [Test]
    public void Build_WithBothOverrides_PopulatesRegistration()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());
        builder.AddDbContext<TestDbContext>();
        builder
            .AddFilterType<TestPlayer, TestPlayerFilterInputType>()
            .AddSortType<TestPlayer, TestPlayerSortInputType>();

        var config = builder.Build();

        config.ModelRegistrations.Should().HaveCount(1);
        config.ModelRegistrations[0].FilterInputType.Should().Be(typeof(TestPlayerFilterInputType));
        config.ModelRegistrations[0].SortInputType.Should().Be(typeof(TestPlayerSortInputType));
    }

    [Test]
    public void Build_NoOverrides_NullFilterAndSort()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());
        builder.AddDbContext<TestDbContext>();

        var config = builder.Build();

        config.ModelRegistrations.Should().HaveCount(1);
        config.ModelRegistrations[0].FilterInputType.Should().BeNull();
        config.ModelRegistrations[0].SortInputType.Should().BeNull();
    }

    [Test]
    public void Build_OverrideForUnregisteredEntity_NoError()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());
        builder.AddFilterType<TestPlayer, TestPlayerFilterInputType>();

        var act = () => builder.Build();

        act.Should().NotThrow();
    }

    [Test]
    public void Build_MultipleEntities_OnlyOverriddenEntityHasCustomType()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());
        builder.AddDbContext<TestDbContext>();
        builder.AddDbContext<SecondDbContext>();
        builder.AddFilterType<TestPlayer, TestPlayerFilterInputType>();

        var config = builder.Build();

        config.ModelRegistrations.Should().HaveCount(2);

        var playerReg = config.ModelRegistrations.Single(r => r.EntityType == typeof(TestPlayer));
        var itemReg = config.ModelRegistrations.Single(r => r.EntityType == typeof(TestItem));

        playerReg.FilterInputType.Should().Be(typeof(TestPlayerFilterInputType));
        itemReg.FilterInputType.Should().BeNull();
    }

    [Test]
    public void Build_OverrideForEntityInMultipleDbContexts_AppliedToAll()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());
        builder.AddDbContext<TestDbContext>();
        builder.AddDbContext<DuplicatePlayerDbContext>();
        builder.AddFilterType<TestPlayer, TestPlayerFilterInputType>();

        var config = builder.Build();

        var playerRegs = config.ModelRegistrations.Where(r => r.EntityType == typeof(TestPlayer));
        playerRegs
            .Should()
            .AllSatisfy(r => r.FilterInputType.Should().Be(typeof(TestPlayerFilterInputType)));
    }

    #endregion

    #region QueryModelRegistration — Record Defaults

    [Test]
    public void QueryModelRegistration_DefaultFilterAndSort_Null()
    {
        var reg = new QueryModelRegistration(
            typeof(TestPlayer),
            typeof(TestDbContext),
            new TraxQueryModelAttribute()
        );

        reg.FilterInputType.Should().BeNull();
        reg.SortInputType.Should().BeNull();
    }

    [Test]
    public void QueryModelRegistration_WithFilterAndSort_Set()
    {
        var reg = new QueryModelRegistration(
            typeof(TestPlayer),
            typeof(TestDbContext),
            new TraxQueryModelAttribute(),
            typeof(TestPlayerFilterInputType),
            typeof(TestPlayerSortInputType)
        );

        reg.FilterInputType.Should().Be(typeof(TestPlayerFilterInputType));
        reg.SortInputType.Should().Be(typeof(TestPlayerSortInputType));
    }

    [Test]
    public void QueryModelRegistration_ExistingFieldsPreserved()
    {
        var attr = new TraxQueryModelAttribute { Description = "Players" };
        var reg = new QueryModelRegistration(
            typeof(TestPlayer),
            typeof(TestDbContext),
            attr,
            typeof(TestPlayerFilterInputType)
        );

        reg.EntityType.Should().Be(typeof(TestPlayer));
        reg.DbContextType.Should().Be(typeof(TestDbContext));
        reg.Attribute.Description.Should().Be("Players");
        reg.FilterInputType.Should().Be(typeof(TestPlayerFilterInputType));
        reg.SortInputType.Should().BeNull();
    }

    #endregion
}

#region Test Filter/Sort Types

public class TestPlayerFilterInputType : FilterInputType<TestPlayer>
{
    protected override void Configure(IFilterInputTypeDescriptor<TestPlayer> descriptor)
    {
        descriptor.BindFieldsExplicitly();
        descriptor.Field(x => x.Name);
    }
}

public class TestPlayerFilterInputTypeAlternate : FilterInputType<TestPlayer>
{
    protected override void Configure(IFilterInputTypeDescriptor<TestPlayer> descriptor)
    {
        descriptor.BindFieldsExplicitly();
        descriptor.Field(x => x.Id);
    }
}

public class TestPlayerSortInputType : SortInputType<TestPlayer>
{
    protected override void Configure(ISortInputTypeDescriptor<TestPlayer> descriptor)
    {
        descriptor.BindFieldsExplicitly();
        descriptor.Field(x => x.Name);
    }
}

public class TestItemFilterInputType : FilterInputType<TestItem>
{
    protected override void Configure(IFilterInputTypeDescriptor<TestItem> descriptor)
    {
        descriptor.BindFieldsExplicitly();
        descriptor.Field(x => x.ItemName);
    }
}

public class TestItemSortInputType : SortInputType<TestItem>
{
    protected override void Configure(ISortInputTypeDescriptor<TestItem> descriptor)
    {
        descriptor.BindFieldsExplicitly();
        descriptor.Field(x => x.ItemName);
    }
}

public class DuplicatePlayerDbContext : DbContext
{
    public DbSet<TestPlayer> Players { get; set; } = null!;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseInMemoryDatabase("DuplicatePlayerDb_" + Guid.NewGuid());
}

#endregion
