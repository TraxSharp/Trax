using FluentAssertions;
using Trax.Api.GraphQL.PersistedOperations.Storage.Exceptions;

namespace Trax.Api.Tests.PersistedOperations.UnitTests.Exceptions;

[TestFixture]
public class PersistedOperationValidationExceptionTests
{
    [Test]
    public void Ctor_WithFailures_PreservesList()
    {
        var failures = new[]
        {
            new ValidationFailure(
                "Cannot query field 'foo'",
                new[] { new ValidationFailureLocation(2, 5) },
                new object[] { "foo" }
            ),
            ValidationFailure.FromMessage("Variable $x is not defined"),
        };

        var ex = new PersistedOperationValidationException(failures);

        ex.Failures.Should().HaveCount(2);
        ex.Failures[0].Message.Should().Be("Cannot query field 'foo'");
        ex.Failures[0]
            .Locations.Should()
            .ContainSingle()
            .Which.Should()
            .Be(new ValidationFailureLocation(2, 5));
        ex.Failures[1].Message.Should().Be("Variable $x is not defined");
        ex.Code.Should().Be("SCHEMA_VALIDATION_FAILED");
    }

    [Test]
    public void Ctor_EmptyFailures_ThrowsArgumentException()
    {
        Action a = () =>
            new PersistedOperationValidationException(Array.Empty<ValidationFailure>());
        a.Should().Throw<ArgumentException>();
    }

    [Test]
    public void Ctor_NullFailures_ThrowsArgumentNullException()
    {
        Action a = () => new PersistedOperationValidationException(null!);
        a.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void Message_SingleFailure_QuotesIt()
    {
        var ex = new PersistedOperationValidationException(
            new[] { ValidationFailure.FromMessage("only one") }
        );

        ex.Message.Should().Contain("only one");
    }

    [Test]
    public void Message_MultipleFailures_ReportsCount()
    {
        var ex = new PersistedOperationValidationException(
            new[]
            {
                ValidationFailure.FromMessage("first"),
                ValidationFailure.FromMessage("second"),
                ValidationFailure.FromMessage("third"),
            }
        );

        ex.Message.Should().Contain("3 errors").And.Contain("first");
    }

    [Test]
    public void InheritsFromPersistedOperationException()
    {
        var ex = new PersistedOperationValidationException(
            new[] { ValidationFailure.FromMessage("x") }
        );

        ex.Should().BeAssignableTo<PersistedOperationException>();
    }
}
