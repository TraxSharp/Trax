using FluentAssertions;
using NUnit.Framework;
using Trax.Effect.Attributes;

namespace Trax.Effect.Tests.Integration.UnitTests.Attributes;

/// <summary>
/// Reflection-shape contract for <see cref="TraxAllowAnonymousAttribute"/>. The
/// attribute is the public opt-in marker that opens a
/// <see cref="TraxQueryModelAttribute"/> entity to anonymous access; downstream
/// discovery code in Trax.Api walks reflection assuming the metadata shape
/// pinned here. A change to <see cref="AttributeUsageAttribute"/> on the
/// attribute (loosening targets, allowing duplicates, dropping inheritance)
/// would silently shift what the discovery validator accepts.
/// </summary>
[TestFixture]
public class TraxAllowAnonymousAttributeTests
{
    [Test]
    public void Attribute_HasUsageWithClassAndInterfaceTargets()
    {
        var usage = typeof(TraxAllowAnonymousAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), inherit: false)
            .Cast<AttributeUsageAttribute>()
            .Single();

        usage.ValidOn.Should().Be(AttributeTargets.Class | AttributeTargets.Interface);
    }

    [Test]
    public void Attribute_AllowMultipleIsFalse()
    {
        var usage = typeof(TraxAllowAnonymousAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), inherit: false)
            .Cast<AttributeUsageAttribute>()
            .Single();

        usage.AllowMultiple.Should().BeFalse();
    }

    [Test]
    public void Attribute_InheritedIsTrue()
    {
        // Inherited = true so a base class or interface declaring
        // [TraxAllowAnonymous] propagates to derived entities. Matches the
        // [TraxAuthorize] inheritance shape so the two attributes compose
        // predictably in a class hierarchy.
        var usage = typeof(TraxAllowAnonymousAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), inherit: false)
            .Cast<AttributeUsageAttribute>()
            .Single();

        usage.Inherited.Should().BeTrue();
    }

    [Test]
    public void Constructor_NoArgs_CreatesInstance()
    {
        var attr = new TraxAllowAnonymousAttribute();

        attr.Should().NotBeNull();
    }

    [Test]
    public void Reflection_AppliedToClass_IsDiscoverable()
    {
        var attr = typeof(DirectlyDecorated)
            .GetCustomAttributes(typeof(TraxAllowAnonymousAttribute), inherit: true)
            .Cast<TraxAllowAnonymousAttribute>()
            .SingleOrDefault();

        attr.Should().NotBeNull();
    }

    [Test]
    public void Reflection_AppliedToBase_PropagatesToDerived()
    {
        var attr = typeof(DerivedFromAnonymousBase)
            .GetCustomAttributes(typeof(TraxAllowAnonymousAttribute), inherit: true)
            .Cast<TraxAllowAnonymousAttribute>()
            .SingleOrDefault();

        attr.Should()
            .NotBeNull("Inherited = true should propagate the attribute to derived classes");
    }

    [Test]
    public void Reflection_AppliedToInterface_IsDiscoverableViaGetInterfaces()
    {
        var iface = typeof(ImplementsAnonymousInterface)
            .GetInterfaces()
            .Single(i => i == typeof(IAnonymouslyReadable));

        var attr = iface
            .GetCustomAttributes(typeof(TraxAllowAnonymousAttribute), inherit: true)
            .Cast<TraxAllowAnonymousAttribute>()
            .SingleOrDefault();

        attr.Should().NotBeNull();
    }

    [TraxAllowAnonymous]
    private class DirectlyDecorated { }

    [TraxAllowAnonymous]
    private class AnonymousBase { }

    private class DerivedFromAnonymousBase : AnonymousBase { }

    [TraxAllowAnonymous]
    private interface IAnonymouslyReadable { }

    private class ImplementsAnonymousInterface : IAnonymouslyReadable { }
}
