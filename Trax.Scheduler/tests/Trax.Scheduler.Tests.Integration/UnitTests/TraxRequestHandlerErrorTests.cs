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
    public void BuildErrorResponse_NonStructuredMessage_ReportsTheTypeOnly()
    {
        var ex = new InvalidOperationException("plain failure");

        var resp = TraxRequestHandler.BuildErrorResponse(ex);

        resp.IsError.Should().BeTrue();
        resp.MetadataId.Should().Be(0);
        resp.ErrorMessage.Should().Be(TraxRequestHandler.UnreportedFailureMessage);
        resp.StackTrace.Should().BeNull();
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
        resp.ErrorMessage.Should().Be(TraxRequestHandler.UnreportedFailureMessage);
        resp.ExceptionType.Should().Be(nameof(Exception));
    }

    [Test]
    public void BuildErrorResponse_RecordShapedMessageOnAnotherExceptionType_IsNotReadAsARecord()
    {
        var message = JsonSerializer.Serialize(
            new TrainExceptionData
            {
                TrainName = "Trax.X.MyTrain",
                TrainExternalId = "ext",
                Type = "ApplicationException",
                Junction = "MyJunction",
                Message = "the inner reason",
                FailureClass = (FailureClass)3,
            }
        );
        var ex = new InvalidOperationException(message);

        var resp = TraxRequestHandler.BuildErrorResponse(ex);

        resp.FailureClass.Should()
            .BeNull("only a TrainException's message is read as a failure record");
        resp.ExceptionType.Should().Be(nameof(InvalidOperationException));
        resp.ErrorMessage.Should()
            .Be(
                TraxRequestHandler.UnreportedFailureMessage,
                "a message that is not a TrainException's is not sent at all"
            );
        resp.FailureJunction.Should().BeNull();
    }

    [Test]
    public void BuildErrorResponse_TrainExceptionMessage_StillCarriesItsClass()
    {
        var ex = new TrainException(
            JsonSerializer.Serialize(
                new TrainExceptionData
                {
                    TrainName = "Trax.X.MyTrain",
                    TrainExternalId = "ext",
                    Type = "HttpRequestException",
                    Junction = "CallJunction",
                    Message = "timed out",
                    FailureClass = FailureClass.Transient,
                }
            )
        );

        var resp = TraxRequestHandler.BuildErrorResponse(ex);

        resp.FailureClass.Should().Be(FailureClass.Transient);
    }

    [Test]
    public void BuildErrorResponse_UndefinedClassInAMessage_IsCarriedAsUnclassified()
    {
        var ex = new TrainException(
            JsonSerializer.Serialize(
                new TrainExceptionData
                {
                    TrainName = "Trax.X.MyTrain",
                    TrainExternalId = "ext",
                    Type = "Exception",
                    Junction = "J",
                    Message = "m",
                    FailureClass = (FailureClass)99,
                }
            )
        );

        var resp = TraxRequestHandler.BuildErrorResponse(ex);

        resp.FailureClass.Should().Be(FailureClass.Unclassified);
    }

    [Test]
    public void BuildErrorResponse_UndefinedClassInAttachedData_IsCarriedAsUnclassified()
    {
        var ex = new InvalidOperationException("boom");
        ex.Data["TrainExceptionData"] = new TrainExceptionData
        {
            TrainName = "Trax.X.MyTrain",
            TrainExternalId = "ext",
            Type = nameof(InvalidOperationException),
            Junction = "J",
            Message = "boom",
            FailureClass = (FailureClass)99,
        };

        var resp = TraxRequestHandler.BuildErrorResponse(ex);

        resp.FailureClass.Should().Be(FailureClass.Unclassified);
    }
}
