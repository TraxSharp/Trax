using System.Text.Json;
using FluentAssertions;
using NUnit.Framework;
using Trax.Core.Exceptions;
using Trax.Scheduler.Services.RequestHandler;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

[TestFixture]
public class TraxRequestHandlerErrorTests
{
    [Test]
    public void BuildErrorResponse_NonStructuredMessage_FallsBackToRawDetails()
    {
        var ex = new InvalidOperationException("plain failure");

        var resp = TraxRequestHandler.BuildErrorResponse(ex);

        resp.IsError.Should().BeTrue();
        resp.MetadataId.Should().Be(0);
        resp.ErrorMessage.Should().Be("plain failure");
        resp.ExceptionType.Should().Be(nameof(InvalidOperationException));
    }

    [Test]
    public void BuildErrorResponse_StructuredJsonMessage_ExtractsTrainExceptionFields()
    {
        var data = new TrainExceptionData
        {
            TrainName = "Trax.X.MyTrain",
            TrainExternalId = "ext",
            Type = "ApplicationException",
            Junction = "MyJunction",
            Message = "the inner reason",
        };
        var ex = new TrainException(JsonSerializer.Serialize(data));

        var resp = TraxRequestHandler.BuildErrorResponse(ex);

        resp.IsError.Should().BeTrue();
        resp.ErrorMessage.Should().Be("the inner reason");
        resp.ExceptionType.Should().Be("ApplicationException");
        resp.FailureJunction.Should().Be("MyJunction");
    }

    [Test]
    public void BuildErrorResponse_NullMessage_DoesNotThrow()
    {
        var ex = new Exception();

        var resp = TraxRequestHandler.BuildErrorResponse(ex);

        resp.IsError.Should().BeTrue();
    }

    [Test]
    public void BuildErrorResponse_GarbageJsonMessage_FallsBackToPlain()
    {
        var ex = new Exception("{not really json");

        var resp = TraxRequestHandler.BuildErrorResponse(ex);

        resp.IsError.Should().BeTrue();
        resp.ErrorMessage.Should().Be("{not really json");
        resp.ExceptionType.Should().Be(nameof(Exception));
    }
}
