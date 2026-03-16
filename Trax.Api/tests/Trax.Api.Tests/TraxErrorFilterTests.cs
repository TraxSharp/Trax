using FluentAssertions;
using HotChocolate;
using Trax.Api.Exceptions;
using Trax.Api.GraphQL.Errors;
using Trax.Core.Exceptions;

namespace Trax.Api.Tests;

[TestFixture]
public class TraxErrorFilterTests
{
    private TraxErrorFilter _filter = null!;

    [SetUp]
    public void SetUp()
    {
        _filter = new TraxErrorFilter();
    }

    #region TrainException

    [Test]
    public void OnError_TrainException_ExposesMessageWithTrainErrorCode()
    {
        var ex = new TrainException("Junction failed: input was invalid");
        var error = CreateError(ex);

        var result = _filter.OnError(error);

        result.Message.Should().Be("Junction failed: input was invalid");
        result.Code.Should().Be("TRAX_TRAIN_ERROR");
    }

    [Test]
    public void OnError_TrainExceptionWithJsonMessage_PreservesFullMessage()
    {
        var json =
            """{"trainName":"My.Train","trainExternalId":"ext-1","type":"ArgumentException","junction":"Validate","message":"Bad input"}""";
        var ex = new TrainException(json);
        var error = CreateError(ex);

        var result = _filter.OnError(error);

        result.Message.Should().Be(json);
        result.Code.Should().Be("TRAX_TRAIN_ERROR");
    }

    #endregion

    #region TrainAuthorizationException

    [Test]
    public void OnError_TrainAuthorizationException_ExposesMessageWithAuthCode()
    {
        var ex = new TrainAuthorizationException("My.Train", "Missing role: Admin");
        var error = CreateError(ex);

        var result = _filter.OnError(error);

        result.Message.Should().Contain("Authorization failed");
        result.Message.Should().Contain("Missing role: Admin");
        result.Code.Should().Be("TRAX_AUTHORIZATION");
    }

    #endregion

    #region InvalidOperationException

    [Test]
    public void OnError_InvalidOperationException_ExposesMessageWithInvalidOpCode()
    {
        var ex = new InvalidOperationException("Train 'My.Train' not found");
        var error = CreateError(ex);

        var result = _filter.OnError(error);

        result.Message.Should().Be("Train 'My.Train' not found");
        result.Code.Should().Be("TRAX_INVALID_OPERATION");
    }

    #endregion

    #region Other Exceptions

    [Test]
    public void OnError_UnknownException_RetainsDefaultMessage()
    {
        var ex = new NullReferenceException("Object reference not set");
        var error = CreateError(ex, "Unexpected Execution Error");

        var result = _filter.OnError(error);

        result.Message.Should().Be("Unexpected Execution Error");
    }

    [Test]
    public void OnError_NoException_RetainsOriginalError()
    {
        var error = ErrorBuilder.New().SetMessage("Some GraphQL validation error").Build();

        var result = _filter.OnError(error);

        result.Message.Should().Be("Some GraphQL validation error");
    }

    #endregion

    #region Helpers

    private static IError CreateError(Exception ex, string? message = null)
    {
        return ErrorBuilder
            .New()
            .SetMessage(message ?? "Unexpected Execution Error")
            .SetException(ex)
            .Build();
    }

    #endregion
}
