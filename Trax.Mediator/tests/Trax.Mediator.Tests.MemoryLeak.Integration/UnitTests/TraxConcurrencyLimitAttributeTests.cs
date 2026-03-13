using FluentAssertions;
using Trax.Effect.Attributes;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.UnitTests;

[TestFixture]
public class TraxConcurrencyLimitAttributeTests
{
    [Test]
    public void Constructor_PositiveValue_SetsMaxConcurrent()
    {
        var attr = new TraxConcurrencyLimitAttribute(15);

        attr.MaxConcurrent.Should().Be(15);
    }

    [Test]
    public void Constructor_One_SetsMaxConcurrent()
    {
        var attr = new TraxConcurrencyLimitAttribute(1);

        attr.MaxConcurrent.Should().Be(1);
    }

    [Test]
    public void Constructor_Zero_ThrowsArgumentOutOfRange()
    {
        var act = () => new TraxConcurrencyLimitAttribute(0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Test]
    public void Constructor_Negative_ThrowsArgumentOutOfRange()
    {
        var act = () => new TraxConcurrencyLimitAttribute(-1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
