using FluentAssertions;
using Trax.Core.Extensions;

namespace Trax.Core.Tests.Unit.UnitTests.Extensions;

public class FunctionalAssertTests : TestSetup
{
    [Test]
    public void AssertLoaded_NonNull_DoesNotThrow()
    {
        string? value = "loaded";

        Action act = () => value.AssertLoaded();

        act.Should().NotThrow();
    }

    [Test]
    public void AssertLoaded_Null_ThrowsWithCallerExpression()
    {
        string? value = null;

        Action act = () => value.AssertLoaded();

        act.Should().Throw<InvalidOperationException>().WithMessage("*value*has not been loaded*");
    }

    [Test]
    public void AssertEachLoaded_AllNonNull_DoesNotThrow()
    {
        var items = new[] { new Holder("a"), new Holder("b") };

        Action act = () => items.AssertEachLoaded(x => x.Value);

        act.Should().NotThrow();
    }

    [Test]
    public void AssertEachLoaded_OneNull_Throws()
    {
        var items = new[] { new Holder("a"), new Holder(null) };

        Action act = () => items.AssertEachLoaded(x => x.Value);

        act.Should().Throw<InvalidOperationException>().WithMessage("*has not been loaded*");
    }

    [Test]
    public void AssertEachLoaded_EmptyCollection_DoesNotThrow()
    {
        var items = Array.Empty<Holder>();

        Action act = () => items.AssertEachLoaded(x => x.Value);

        act.Should().NotThrow();
    }

    private class Holder
    {
        public Holder(string? value) => Value = value;

        public string? Value { get; }
    }
}
