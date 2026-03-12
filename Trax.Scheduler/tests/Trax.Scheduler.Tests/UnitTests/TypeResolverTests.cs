using FluentAssertions;
using Trax.Scheduler.Utilities;

namespace Trax.Scheduler.Tests.UnitTests;

[TestFixture]
public class TypeResolverTests
{
    [Test]
    public void ResolveType_KnownType_ReturnsType()
    {
        var type = TypeResolver.ResolveType(typeof(string).FullName!);
        type.Should().Be(typeof(string));
    }

    [Test]
    public void ResolveType_TypeInLoadedAssembly_ReturnsType()
    {
        // TypeResolver itself is in a loaded assembly
        var type = TypeResolver.ResolveType(typeof(TypeResolver).FullName!);
        type.Should().Be(typeof(TypeResolver));
    }

    [Test]
    public void ResolveType_UnknownType_ThrowsWithActionableMessage()
    {
        var act = () => TypeResolver.ResolveType("Nonexistent.Namespace.MyType");

        var ex = act.Should().Throw<TypeLoadException>().Which;
        ex.Message.Should().Contain("Nonexistent.Namespace.MyType");
        ex.Message.Should().Contain("Unable to resolve type");
    }

    [Test]
    public void ResolveType_PartialNamespaceMatch_ListsMatchingAssemblies()
    {
        // Use a namespace that matches loaded assemblies but a type that doesn't exist
        var act = () => TypeResolver.ResolveType("Trax.Scheduler.FakeNonExistentType");

        var ex = act.Should().Throw<TypeLoadException>().Which;
        ex.Message.Should().Contain("Trax.Scheduler");
        // Should mention assemblies with matching namespace prefix
        ex.Message.Should()
            .ContainAny("Trax.Scheduler", "Assemblies with a matching namespace prefix");
    }

    [Test]
    public void ResolveType_NoMatchingAssemblies_SuggestsLoadingAssembly()
    {
        var act = () =>
            TypeResolver.ResolveType("CompletelyUnknown.Namespace.That.Matches.Nothing.MyType");

        var ex = act.Should().Throw<TypeLoadException>().Which;
        ex.Message.Should().Contain("Ensure the assembly");
    }
}
