using Trax.Core.Extensions;
using FluentAssertions;
using LanguageExt;

namespace Trax.Core.Tests.Unit.UnitTests.Extensions;

public class FunctionalTests : TestSetup
{
    [Theory]
    public async Task TestUnwrapTaskEitherRight()
    {
        // Arrange
        var either = Task.FromResult<Either<Exception, int>>(Prelude.Right<Exception, int>(42));

        // Act
        var result = await either.Unwrap();

        // Assert
        result.Should().Be(42);
    }

    [Theory]
    public async Task TestUnwrapTaskEitherLeft()
    {
        // Arrange
        var exception = new InvalidOperationException("Test exception");
        var either = Task.FromResult<Either<Exception, int>>(
            Prelude.Left<Exception, int>(exception)
        );

        // Act
        Func<Task> act = async () => await either.Unwrap();

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Test exception");
    }

    [Theory]
    public void TestUnwrapEitherRight()
    {
        // Arrange
        var either = Prelude.Right<Exception, int>(42);

        // Act
        var result = either.Unwrap();

        // Assert
        result.Should().Be(42);
    }

    [Theory]
    public void TestUnwrapEitherLeft()
    {
        // Arrange
        var exception = new InvalidOperationException("Test exception");
        var either = Prelude.Left<Exception, int>(exception);

        // Act
        Action act = () => either.Unwrap();

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("Test exception");
    }

    [Theory]
    public async Task TestUnwrapTaskEitherRightDifferentTypes()
    {
        // Arrange
        var either = Task.FromResult<Either<Exception, string>>(
            Prelude.Right<Exception, string>("success")
        );

        // Act
        var result = await either.Unwrap();

        // Assert
        result.Should().Be("success");
    }

    [Theory]
    public void TestUnwrapEitherRightDifferentTypes()
    {
        // Arrange
        var either = Prelude.Right<Exception, double>(3.14);

        // Act
        var result = either.Unwrap();

        // Assert
        result.Should().Be(3.14);
    }

    [Theory]
    public async Task TestUnwrapTaskEitherLeftDifferentTypes()
    {
        // Arrange
        var exception = new ArgumentNullException("param");
        var either = Task.FromResult<Either<ArgumentException, bool>>(
            Prelude.Left<ArgumentException, bool>(exception)
        );

        // Act
        Func<Task> act = async () => await either.Unwrap();

        // Assert
        await act.Should()
            .ThrowAsync<ArgumentNullException>()
            .WithMessage("Value cannot be null. (Parameter 'param')");
    }

    [Theory]
    public void TestUnwrapEitherLeftDifferentTypes()
    {
        // Arrange
        var exception = new ArgumentException("Test exception");
        var either = Prelude.Left<Exception, char>(exception);

        // Act
        Action act = () => either.Unwrap();

        // Assert
        act.Should().Throw<ArgumentException>().WithMessage("Test exception");
    }
}
