using FluentAssertions;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using Trax.Core.Exceptions;
using Trax.Core.Junction;
using Trax.Core.Train;

namespace Trax.Core.Tests.Unit.UnitTests.Step;

/// <summary>
/// Verifies that original exception identity (message, stack trace, type) is preserved
/// through the full Junction → Monad → ResolveOrThrow chain.
/// </summary>
public class StackTracePreservationTests : TestSetup
{
    #region ResolveOrThrow Stack Trace Preservation

    [Theory]
    public async Task Run_ThrowingJunction_PreservesOriginalStackTrace()
    {
        // Arrange
        var train = new ThrowingTrain();

        // Act
        Exception? caught = null;
        try
        {
            await train.Run("input");
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        // Assert — the stack trace should contain the original throw site
        caught.Should().NotBeNull();
        caught!.StackTrace.Should().Contain(nameof(AlwaysThrowsJunction));
    }

    [Theory]
    public async Task Run_ThrowingJunction_StackTraceDoesNotStartAtResolveOrThrow()
    {
        // Arrange
        var train = new ThrowingTrain();

        // Act
        Exception? caught = null;
        try
        {
            await train.Run("input");
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        // Assert — the first frame should NOT be ResolveOrThrow
        caught.Should().NotBeNull();
        var firstLine = caught!.StackTrace!.Split('\n')[0];
        firstLine.Should().NotContain("ResolveOrThrow");
    }

    #endregion

    #region RunEither (Either Path) Stack Trace Preservation

    [Theory]
    public async Task RunEither_ThrowingJunction_ReturnsExceptionWithOriginalStackTrace()
    {
        // Arrange
        var train = new ThrowingTrain();

        // Act
        var result = await train.RunEither("input");

        // Assert
        result.IsLeft.Should().BeTrue();
        var exception = result.Swap().ValueUnsafe();
        exception.StackTrace.Should().Contain(nameof(AlwaysThrowsJunction));
    }

    #endregion

    #region Original Message Preservation

    [Theory]
    public async Task Run_ThrowingJunction_ExceptionHasOriginalMessage()
    {
        // Arrange
        var train = new ThrowingTrain();

        // Act
        Exception? caught = null;
        try
        {
            await train.Run("input");
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        // Assert — the message should be the original, not JSON
        caught.Should().NotBeNull();
        caught!.Message.Should().Be("something went wrong");
        caught.Should().BeOfType<InvalidOperationException>();
    }

    [Theory]
    public async Task Run_ThrowingJunction_ExceptionDataContainsStructuredInfo()
    {
        // Arrange
        var train = new ThrowingTrain();

        // Act
        Exception? caught = null;
        try
        {
            await train.Run("input");
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        // Assert — structured data available via Exception.Data
        caught.Should().NotBeNull();
        var data = caught!.Data["TrainExceptionData"] as TrainExceptionData;
        data.Should().NotBeNull();
        data!.Junction.Should().Be(nameof(AlwaysThrowsJunction));
        data.Type.Should().Be("InvalidOperationException");
        data.Message.Should().Be("something went wrong");
        data.StackTrace.Should().Contain(nameof(AlwaysThrowsJunction));
    }

    #endregion

    #region Exception Type Preservation

    [Theory]
    public async Task Run_ThrowingJunction_OriginalExceptionTypePreserved()
    {
        // Arrange
        var train = new ArgumentThrowingTrain();

        // Act
        Exception? caught = null;
        try
        {
            await train.Run("input");
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        // Assert — the exception type should be the original, not TrainException
        caught.Should().NotBeNull();
        caught.Should().BeOfType<ArgumentException>();
        caught!.Message.Should().Be("bad argument");
    }

    #endregion

    #region Test Helpers

    private class AlwaysThrowsJunction : Junction<string, string>
    {
        public override Task<string> Run(string input) =>
            throw new InvalidOperationException("something went wrong");
    }

    private class ArgumentThrowsJunction : Junction<string, string>
    {
        public override Task<string> Run(string input) =>
            throw new ArgumentException("bad argument");
    }

    private class ThrowingTrain : Train<string, string>
    {
        protected override Task<Either<Exception, string>> Junctions() =>
            Chain<AlwaysThrowsJunction>().Resolve();
    }

    private class ArgumentThrowingTrain : Train<string, string>
    {
        protected override Task<Either<Exception, string>> Junctions() =>
            Chain<ArgumentThrowsJunction>().Resolve();
    }

    #endregion
}
