using FluentAssertions;
using HotChocolate.Execution.Configuration;
using HotChocolate.Types;
using HotChocolate.Types.Descriptors;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.Configuration;
using Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;

namespace Trax.Api.Tests;

[TestFixture]
public class TypeModuleRegistrationTests
{
    #region AddTypeModule — Builder Storage

    [Test]
    public void AddTypeModule_SingleModule_StoredInBuilder()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());

        builder.AddTypeModule<StubTypeModuleA>();

        builder
            .AdditionalTypeModules.Should()
            .ContainSingle()
            .Which.Should()
            .Be(typeof(StubTypeModuleA));
    }

    [Test]
    public void AddTypeModule_MultipleCalls_AllStored()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());

        builder.AddTypeModule<StubTypeModuleA>();
        builder.AddTypeModule<StubTypeModuleB>();

        builder.AdditionalTypeModules.Should().HaveCount(2);
        builder
            .AdditionalTypeModules.Should()
            .ContainInOrder(typeof(StubTypeModuleA), typeof(StubTypeModuleB));
    }

    [Test]
    public void AddTypeModule_ReturnsSameBuilder()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());

        var result = builder.AddTypeModule<StubTypeModuleA>();

        result.Should().BeSameAs(builder);
    }

    [Test]
    public void AddTypeModule_FluentChaining_AllModulesStored()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());

        builder.AddTypeModule<StubTypeModuleA>().AddTypeModule<StubTypeModuleB>();

        builder.AdditionalTypeModules.Should().HaveCount(2);
    }

    [Test]
    public void AddTypeModule_SameModuleTwice_BothStored()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());

        builder.AddTypeModule<StubTypeModuleA>();
        builder.AddTypeModule<StubTypeModuleA>();

        builder.AdditionalTypeModules.Should().HaveCount(2);
    }

    #endregion

    #region Build — Propagation to GraphQLConfiguration

    [Test]
    public void Build_WithTypeModules_PropagatedToConfig()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());
        builder.AddTypeModule<StubTypeModuleA>();
        builder.AddTypeModule<StubTypeModuleB>();

        var config = builder.Build();

        config.AdditionalTypeModules.Should().HaveCount(2);
        config
            .AdditionalTypeModules.Should()
            .ContainInOrder(typeof(StubTypeModuleA), typeof(StubTypeModuleB));
    }

    [Test]
    public void Build_NoTypeModules_EmptyList()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());

        var config = builder.Build();

        config.AdditionalTypeModules.Should().BeEmpty();
    }

    [Test]
    public void Build_TypeModulesAndDbContext_BothPreserved()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());
        builder.AddDbContext<TestDbContext>();
        builder.AddTypeModule<StubTypeModuleA>();

        var config = builder.Build();

        config.ModelRegistrations.Should().HaveCount(1);
        config.AdditionalTypeModules.Should().HaveCount(1);
    }

    #endregion

    #region GraphQLConfiguration — AdditionalTypeModules Property

    [Test]
    public void GraphQLConfiguration_WithTypeModules_ExposesReadOnlyList()
    {
        var types = new List<Type> { typeof(StubTypeModuleA), typeof(StubTypeModuleB) };
        var config = new GraphQLConfiguration([], types, [], []);

        config.AdditionalTypeModules.Should().HaveCount(2);
        config.AdditionalTypeModules[0].Should().Be(typeof(StubTypeModuleA));
        config.AdditionalTypeModules[1].Should().Be(typeof(StubTypeModuleB));
    }

    [Test]
    public void GraphQLConfiguration_EmptyTypeModules_EmptyList()
    {
        var config = new GraphQLConfiguration([], [], [], []);

        config.AdditionalTypeModules.Should().BeEmpty();
    }

    #endregion

    #region Stubs

    private class StubTypeModuleA : TypeModule
    {
        public override ValueTask<IReadOnlyCollection<ITypeSystemMember>> CreateTypesAsync(
            IDescriptorContext context,
            CancellationToken cancellationToken
        ) => new(Array.Empty<ITypeSystemMember>());
    }

    private class StubTypeModuleB : TypeModule
    {
        public override ValueTask<IReadOnlyCollection<ITypeSystemMember>> CreateTypesAsync(
            IDescriptorContext context,
            CancellationToken cancellationToken
        ) => new(Array.Empty<ITypeSystemMember>());
    }

    #endregion
}
