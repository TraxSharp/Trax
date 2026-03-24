using FluentAssertions;
using HotChocolate.Execution.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.Configuration;
using Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;

namespace Trax.Api.Tests;

[TestFixture]
public class SchemaConfigurationTests
{
    #region ConfigureSchema — Builder Storage

    [Test]
    public void ConfigureSchema_SingleCallback_Stored()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());

        builder.ConfigureSchema(_ => { });

        builder.SchemaConfigurations.Should().HaveCount(1);
    }

    [Test]
    public void ConfigureSchema_MultipleCallbacks_AllStoredInOrder()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());
        var callOrder = new List<int>();

        builder.ConfigureSchema(_ => callOrder.Add(1));
        builder.ConfigureSchema(_ => callOrder.Add(2));
        builder.ConfigureSchema(_ => callOrder.Add(3));

        builder.SchemaConfigurations.Should().HaveCount(3);

        // Verify order is preserved by invoking them
        foreach (var action in builder.SchemaConfigurations)
            action(null!);

        callOrder.Should().ContainInOrder(1, 2, 3);
    }

    [Test]
    public void ConfigureSchema_ReturnsSameBuilder()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());

        var result = builder.ConfigureSchema(_ => { });

        result.Should().BeSameAs(builder);
    }

    [Test]
    public void ConfigureSchema_FluentChaining_AllCallbacksStored()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());

        builder.ConfigureSchema(_ => { }).ConfigureSchema(_ => { }).ConfigureSchema(_ => { });

        builder.SchemaConfigurations.Should().HaveCount(3);
    }

    #endregion

    #region Build — Propagation to GraphQLConfiguration

    [Test]
    public void Build_WithSchemaConfigurations_PropagatedToConfig()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());
        Action<IRequestExecutorBuilder> callback1 = _ => { };
        Action<IRequestExecutorBuilder> callback2 = _ => { };

        builder.ConfigureSchema(callback1);
        builder.ConfigureSchema(callback2);

        var config = builder.Build();

        config.SchemaConfigurations.Should().HaveCount(2);
        config.SchemaConfigurations[0].Should().BeSameAs(callback1);
        config.SchemaConfigurations[1].Should().BeSameAs(callback2);
    }

    [Test]
    public void Build_NoSchemaConfigurations_EmptyList()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());

        var config = builder.Build();

        config.SchemaConfigurations.Should().BeEmpty();
    }

    [Test]
    public void Build_SchemaConfigurationsWithDbContext_BothPreserved()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());
        builder.AddDbContext<TestDbContext>();
        builder.ConfigureSchema(_ => { });

        var config = builder.Build();

        config.ModelRegistrations.Should().HaveCount(1);
        config.SchemaConfigurations.Should().HaveCount(1);
    }

    #endregion

    #region GraphQLConfiguration — SchemaConfigurations Property

    [Test]
    public void GraphQLConfiguration_WithSchemaConfigurations_ExposesReadOnlyList()
    {
        var callbacks = new List<Action<IRequestExecutorBuilder>> { _ => { }, _ => { } };
        var config = new GraphQLConfiguration([], [], callbacks);

        config.SchemaConfigurations.Should().HaveCount(2);
    }

    [Test]
    public void GraphQLConfiguration_EmptySchemaConfigurations_EmptyList()
    {
        var config = new GraphQLConfiguration([], [], []);

        config.SchemaConfigurations.Should().BeEmpty();
    }

    #endregion

    #region Combined Builder — All Features Together

    [Test]
    public void Build_AllFeaturesConfigured_AllPropagated()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());
        builder.AddDbContext<TestDbContext>();
        builder.AddFilterType<TestPlayer, TestPlayerFilterInputType>();
        builder.AddSortType<TestPlayer, TestPlayerSortInputType>();
        builder.AddTypeModule<StubTypeModuleForSchema>();
        builder.ConfigureSchema(_ => { });

        var config = builder.Build();

        config.ModelRegistrations.Should().HaveCount(1);
        config.ModelRegistrations[0].FilterInputType.Should().Be(typeof(TestPlayerFilterInputType));
        config.ModelRegistrations[0].SortInputType.Should().Be(typeof(TestPlayerSortInputType));
        config.AdditionalTypeModules.Should().ContainSingle();
        config.SchemaConfigurations.Should().ContainSingle();
    }

    [Test]
    public void Build_AllFeaturesViaFluentChain_AllPropagated()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());

        builder
            .AddDbContext<TestDbContext>()
            .AddFilterType<TestPlayer, TestPlayerFilterInputType>()
            .AddSortType<TestPlayer, TestPlayerSortInputType>()
            .AddTypeModule<StubTypeModuleForSchema>()
            .ConfigureSchema(_ => { });

        var config = builder.Build();

        config.ModelRegistrations.Should().HaveCount(1);
        config.AdditionalTypeModules.Should().HaveCount(1);
        config.SchemaConfigurations.Should().HaveCount(1);
    }

    #endregion

    #region Stubs

    private class StubTypeModuleForSchema : HotChocolate.Execution.Configuration.TypeModule
    {
        public override ValueTask<
            IReadOnlyCollection<HotChocolate.Types.ITypeSystemMember>
        > CreateTypesAsync(
            HotChocolate.Types.Descriptors.IDescriptorContext context,
            CancellationToken cancellationToken
        ) => new(Array.Empty<HotChocolate.Types.ITypeSystemMember>());
    }

    #endregion
}
