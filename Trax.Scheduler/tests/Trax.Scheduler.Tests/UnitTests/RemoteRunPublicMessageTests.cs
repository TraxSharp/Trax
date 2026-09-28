using System.Text.Json;
using FluentAssertions;
using Trax.Core.Exceptions;
using Trax.Scheduler.Services.RequestHandler;
using Trax.Scheduler.Services.RunExecutor;

namespace Trax.Scheduler.Tests.UnitTests;

/// <summary>
/// The message a remote run's failure offers the caller's client. The runner decides it, from
/// the exception it caught, and sends it as <c>RemoteRunResponse.PublicMessage</c>; the calling
/// side rebuilds the failure as a <see cref="RemoteRunException"/> carrying it, so a surface
/// such as the GraphQL error filter reads one property instead of recognising message formats.
/// Only a train author's own <see cref="TrainException"/> message is offered; anything else is
/// offered nothing, and a client is told only that the train failed.
///
/// <para>Enforces <c>Trax.Docs/adr/0028-a-remote-runs-client-message-is-chosen-by-the-runner.md</c>.</para>
/// </summary>
[Property("adr", "Trax.Docs/adr/0028-a-remote-runs-client-message-is-chosen-by-the-runner.md")]
[TestFixture]
public class RemoteRunPublicMessageTests
{
    private const string Because =
        "only a train author's TrainException message is offered to a client "
        + "(Trax.Docs/adr/0028-a-remote-runs-client-message-is-chosen-by-the-runner.md)";

    [Test]
    public void A_train_authors_TrainException_offers_its_message()
    {
        var response = TraxRequestHandler.BuildErrorResponse(
            new TrainException("Order 42 is already closed.")
        );

        response.PublicMessage.Should().Be("Order 42 is already closed.", Because);
    }

    [Test]
    public void A_TrainException_raised_in_a_junction_offers_its_message()
    {
        var failure = new TrainException("Order 42 is already closed.");
        failure.Data["TrainExceptionData"] = new TrainExceptionData
        {
            TrainName = "Orders.ICloseOrderTrain",
            TrainExternalId = "abc",
            Type = nameof(TrainException),
            Junction = "CloseOrder",
            Message = "Order 42 is already closed.",
        };

        var response = TraxRequestHandler.BuildErrorResponse(failure);

        response.PublicMessage.Should().Be("Order 42 is already closed.", Because);
    }

    [Test]
    public void A_carried_TrainException_offers_its_message()
    {
        var carried = JsonSerializer.Serialize(
            new TrainExceptionData
            {
                TrainName = "Orders.ICloseOrderTrain",
                TrainExternalId = "abc",
                Type = nameof(TrainException),
                Junction = "CloseOrder",
                Message = "Order 42 is already closed.",
            }
        );

        var response = TraxRequestHandler.BuildErrorResponse(new TrainException(carried));

        response.PublicMessage.Should().Be("Order 42 is already closed.", Because);
    }

    private static IEnumerable<TestCaseData> FailuresThatOfferNothing()
    {
        yield return new TestCaseData(
            new InvalidOperationException("Connection to 10.0.0.5:5432 refused")
        ).SetArgDisplayNames("another exception type");

        var inJunction = new InvalidOperationException("Connection to 10.0.0.5:5432 refused");
        inJunction.Data["TrainExceptionData"] = new TrainExceptionData
        {
            TrainName = "Orders.ICloseOrderTrain",
            TrainExternalId = "abc",
            Type = "NpgsqlException",
            Junction = "CloseOrder",
            Message = "Connection to 10.0.0.5:5432 refused",
        };
        yield return new TestCaseData(inJunction).SetArgDisplayNames(
            "another exception type raised in a junction"
        );

        yield return new TestCaseData(
            new TrainException(
                JsonSerializer.Serialize(
                    new TrainExceptionData
                    {
                        TrainName = "Orders.ICloseOrderTrain",
                        TrainExternalId = "abc",
                        Type = "NpgsqlException",
                        Junction = "CloseOrder",
                        Message = "Connection to 10.0.0.5:5432 refused",
                    }
                )
            )
        ).SetArgDisplayNames("a carried exception of another type");

        yield return new TestCaseData(
            new DerivedTrainException("SELECT * FROM orders failed")
        ).SetArgDisplayNames("a type derived from TrainException");

        yield return new TestCaseData(
            new RemoteRunException("Remote run endpoint returned HTTP 502: <html>")
        ).SetArgDisplayNames("a remote failure passing through this runner");
    }

    [TestCaseSource(nameof(FailuresThatOfferNothing))]
    public void Any_other_failure_offers_no_public_message(Exception failure)
    {
        var response = TraxRequestHandler.BuildErrorResponse(failure);

        response.IsError.Should().BeTrue();
        response.PublicMessage.Should().BeNull(Because);
    }

    [Test]
    public void The_rebuilt_failure_carries_the_runners_public_message()
    {
        var response = new RemoteRunResponse(
            MetadataId: 7,
            IsError: true,
            ErrorMessage: "Order 42 is already closed.",
            ExceptionType: nameof(TrainException),
            FailureJunction: "CloseOrder"
        )
        {
            PublicMessage = "Order 42 is already closed.",
        };

        var rebuilt = response.ToTrainException();

        rebuilt
            .Should()
            .BeOfType<RemoteRunException>()
            .Which.PublicMessage.Should()
            .Be("Order 42 is already closed.");
    }

    [Test]
    public void The_rebuilt_failure_has_no_public_message_when_the_runner_sent_none()
    {
        var response = new RemoteRunResponse(
            MetadataId: 7,
            IsError: true,
            ErrorMessage: "Order 42 is already closed.",
            ExceptionType: nameof(TrainException)
        );

        var rebuilt = response.ToTrainException();

        rebuilt
            .Should()
            .BeOfType<RemoteRunException>()
            .Which.PublicMessage.Should()
            .BeNull("a runner that predates the field sends none, and nothing is guessed");
    }

    [Test]
    public void The_public_message_crosses_the_wire()
    {
        var sent = new RemoteRunResponse(
            MetadataId: 7,
            IsError: true,
            ErrorMessage: "Order 42 is already closed.",
            ExceptionType: nameof(TrainException)
        )
        {
            PublicMessage = "Order 42 is already closed.",
        };

        var json = JsonSerializer.Serialize(sent, RemoteRunJson.Write);
        var received = JsonSerializer.Deserialize<RemoteRunResponse>(json, RemoteRunJson.Read);

        json.Should().Contain("\"publicMessage\":");
        received.Should().Be(sent);
    }

    private sealed class DerivedTrainException(string message) : TrainException(message);
}
