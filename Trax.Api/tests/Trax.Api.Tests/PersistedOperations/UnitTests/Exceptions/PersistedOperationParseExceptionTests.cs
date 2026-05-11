using FluentAssertions;
using Trax.Api.GraphQL.PersistedOperations.Storage.Exceptions;

namespace Trax.Api.Tests.PersistedOperations.UnitTests.Exceptions;

[TestFixture]
public class PersistedOperationParseExceptionTests
{
    [Test]
    public void Ctor_WithLineAndColumn_PreservesValues()
    {
        var ex = new PersistedOperationParseException("unexpected '!'", line: 3, column: 12);

        ex.Line.Should().Be(3);
        ex.Column.Should().Be(12);
        ex.OriginalMessage.Should().Be("unexpected '!'");
        ex.Code.Should().Be("PARSE_FAILED");
        ex.Message.Should()
            .Contain("line 3")
            .And.Contain("column 12")
            .And.Contain("unexpected '!'");
    }

    [Test]
    public void Ctor_WithoutLineColumn_OmitsThemFromMessage()
    {
        var ex = new PersistedOperationParseException("bad syntax", line: null, column: null);

        ex.Line.Should().BeNull();
        ex.Column.Should().BeNull();
        ex.Message.Should().Contain("bad syntax").And.NotContain("line").And.NotContain("column");
    }

    [Test]
    public void Ctor_NullOrWhitespaceMessage_ThrowsArgumentException()
    {
        Action a = () => new PersistedOperationParseException(null!, 1, 1);
        Action b = () => new PersistedOperationParseException("", 1, 1);
        Action c = () => new PersistedOperationParseException("   ", 1, 1);

        a.Should().Throw<ArgumentException>();
        b.Should().Throw<ArgumentException>();
        c.Should().Throw<ArgumentException>();
    }

    [Test]
    public void InheritsFromPersistedOperationException()
    {
        var ex = new PersistedOperationParseException("x", 1, 1);

        ex.Should().BeAssignableTo<PersistedOperationException>();
        ex.Should().BeAssignableTo<InvalidOperationException>();
    }

    [Test]
    public void Ctor_WithInnerException_PreservesInner()
    {
        var inner = new InvalidOperationException("root cause");
        var ex = new PersistedOperationParseException("bad", 1, 1, inner);

        ex.InnerException.Should().BeSameAs(inner);
    }
}
