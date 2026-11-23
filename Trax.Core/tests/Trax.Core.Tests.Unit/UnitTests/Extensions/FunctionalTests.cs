using AwesomeAssertions;
using Trax.Core.Extensions;
using Trax.Core.Functional;

namespace Trax.Core.Tests.Unit.UnitTests.Extensions;

public class FunctionalTests : TestSetup
{
    [Theory]
    public async Task TestUnwrapTaskEitherRight()
    {
        // Arrange
        var either = Task.FromResult<Either<Exception, int>>(Either<Exception, int>.Right(42));

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
            Either<Exception, int>.Left(exception)
        );

        // Act
        Func<Task> act = async () => await either.Unwrap().ConfigureAwait(false);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Test exception");
    }

    [Theory]
    public void TestUnwrapEitherRight()
    {
        // Arrange
        var either = Either<Exception, int>.Right(42);

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
        var either = Either<Exception, int>.Left(exception);

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
            Either<Exception, string>.Right("success")
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
        var either = Either<Exception, double>.Right(3.14);

        // Act
        var result = either.Unwrap();

        // Assert
        result.Should().Be(3.14);
    }

    [Theory]
    public async Task TestUnwrapTaskEitherLeftDifferentTypes()
    {
        // Arrange
        var exception = new FileNotFoundException("missing");
        var either = Task.FromResult<Either<IOException, bool>>(
            Either<IOException, bool>.Left(exception)
        );

        // Act
        Func<Task> act = async () => await either.Unwrap().ConfigureAwait(false);

        // Assert
        await act.Should().ThrowAsync<FileNotFoundException>().WithMessage("missing");
    }

    [Theory]
    public void TestUnwrapEitherLeftDifferentTypes()
    {
        // Arrange
        var exception = new InvalidOperationException("Test exception");
        var either = Either<Exception, char>.Left(exception);

        // Act
        Action act = () => either.Unwrap();

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("Test exception");
    }
}
