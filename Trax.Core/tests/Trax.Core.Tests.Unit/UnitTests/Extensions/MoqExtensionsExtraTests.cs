using FluentAssertions;
using Trax.Core.Extensions;

namespace Trax.Core.Tests.Unit.UnitTests.Extensions;

public class MoqExtensionsExtraTests : TestSetup
{
    [Test]
    public void GetMockedTypeFromObject_NoMockProperty_ReturnsNull()
    {
        var ordinaryObject = new PlainClass();

        var result = ordinaryObject.GetMockedTypeFromObject();

        ((object?)result).Should().BeNull();
    }

    [Test]
    public void GetMockedTypeFromObject_MockPropertyReturnsNull_ReturnsNull()
    {
        var obj = new HasNullMockProperty();

        var result = obj.GetMockedTypeFromObject();

        ((object?)result).Should().BeNull();
    }

    [Test]
    public void GetMockedTypeFromObject_MockHasNoMockedTypeProperty_ReturnsNull()
    {
        var obj = new HasMockWithoutMockedType();

        var result = obj.GetMockedTypeFromObject();

        ((object?)result).Should().BeNull();
    }

    [Test]
    public void GetMockedTypeFromObject_NullArgument_Throws()
    {
        object obj = null!;

        Action act = () => obj.GetMockedTypeFromObject();

        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void IsMoqProxy_NonMoqType_ReturnsFalse()
    {
        var type = typeof(string);

        type.IsMoqProxy().Should().BeFalse();
    }

    private class PlainClass { }

    private class HasNullMockProperty
    {
        public object? Mock => null;
    }

    private class HasMockWithoutMockedType
    {
        public object Mock { get; } = new object();
    }
}
