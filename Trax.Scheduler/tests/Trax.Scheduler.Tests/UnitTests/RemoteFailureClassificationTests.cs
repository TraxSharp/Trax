using System.Text.Json;
using FluentAssertions;
using Trax.Core.Exceptions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Scheduler.Services.RequestHandler;
using Trax.Scheduler.Services.RunExecutor;

namespace Trax.Scheduler.Tests.UnitTests;

/// <summary>
/// A run executed on a remote worker has to bring its failure classification home. The worker
/// classifies while it still holds the real exception; by the time the calling side sees the
/// failure it has been rebuilt from JSON and the original type is gone, so re-classifying there
/// would mean matching on a type name — exactly what classification exists to avoid.
///
/// <para>Enforces Trax.Docs/adr/0020-a-failure-is-classified-where-it-happens-and-carried.md.</para>
/// </summary>
[Property("adr", "Trax.Docs/adr/0020-a-failure-is-classified-where-it-happens-and-carried.md")]
[TestFixture]
public class RemoteFailureClassificationTests
{
    private static Exception WithAttachedData(FailureClass? failureClass)
    {
        var exception = new InvalidOperationException("the remote thing went wrong");
        exception.Data["TrainExceptionData"] = new TrainExceptionData
        {
            TrainName = "SomeTrain",
            TrainExternalId = "abc",
            Type = nameof(InvalidOperationException),
            Junction = "SomeJunction",
            Message = "the remote thing went wrong",
            FailureClass = failureClass,
        };
        return exception;
    }

    [Test]
    public void The_worker_response_carries_the_classification_off_the_exception()
    {
        var response = TraxRequestHandler.BuildErrorResponse(
            WithAttachedData(FailureClass.Conflict)
        );

        response.FailureClass.Should().Be(FailureClass.Conflict);
        response
            .FailureJunction.Should()
            .Be(
                "SomeJunction",
                "reading the attached data also keeps the junction, which the message-only path lost"
            );
    }

    [Test]
    public void An_unclassified_failure_carries_no_classification()
    {
        TraxRequestHandler
            .BuildErrorResponse(WithAttachedData(null))
            .FailureClass.Should()
            .BeNull();
    }

    [Test]
    public void A_plain_exception_carries_no_classification()
    {
        var response = TraxRequestHandler.BuildErrorResponse(new Exception("no structure here"));

        response.FailureClass.Should().BeNull();
        response.ExceptionType.Should().Be(nameof(Exception));
    }

    [Test]
    public void A_failure_that_already_crossed_a_boundary_still_carries_it()
    {
        var json = JsonSerializer.Serialize(
            new TrainExceptionData
            {
                TrainName = "SomeTrain",
                TrainExternalId = "abc",
                Type = nameof(InvalidOperationException),
                Junction = "SomeJunction",
                Message = "already serialized once",
                FailureClass = FailureClass.Transient,
            }
        );

        TraxRequestHandler
            .BuildErrorResponse(new Exception(json))
            .FailureClass.Should()
            .Be(FailureClass.Transient);
    }

    [Test]
    public void The_calling_side_records_the_classification_without_re_deriving_it()
    {
        // The whole round trip: worker exception → response → rebuilt exception → metadata.
        var response = TraxRequestHandler.BuildErrorResponse(
            WithAttachedData(FailureClass.Permanent)
        );

        var rebuilt = HttpRunExecutor.BuildExceptionFromErrorResponse(response);

        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = "SomeTrain",
                ExternalId = "abc",
                Input = null,
            }
        );
        metadata.AddException(rebuilt);

        metadata.FailureClass.Should().Be(FailureClass.Permanent);
        metadata
            .FailureJunction.Should()
            .Be("SomeJunction", "the rest of the structured failure survives the trip too");
    }

    [Test]
    public void A_worker_that_sent_no_classification_records_Unclassified()
    {
        var rebuilt = HttpRunExecutor.BuildExceptionFromErrorResponse(
            TraxRequestHandler.BuildErrorResponse(WithAttachedData(null))
        );

        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = "SomeTrain",
                ExternalId = "abc",
                Input = null,
            }
        );
        metadata.AddException(rebuilt);

        metadata
            .FailureClass.Should()
            .Be(FailureClass.Unclassified, "an older worker sends nothing and must not break");
    }

    [Test]
    public void A_response_from_a_worker_that_predates_classification_still_reads()
    {
        // Written by hand rather than by the current record, which is what an older worker
        // sends: the same shape with no failureClass property at all.
        const string olderWorkerJson = """
            {
              "metadataId": 7,
              "externalId": "abc",
              "isError": true,
              "errorMessage": "boom",
              "exceptionType": "InvalidOperationException",
              "failureJunction": "DoThing"
            }
            """;

        // Read with the executors' own options, so the reader's converters are part of what is
        // proven to tolerate the missing property.
        var response = JsonSerializer.Deserialize<RemoteRunResponse>(
            olderWorkerJson,
            RemoteRunJson.Read
        )!;
        var rebuilt = HttpRunExecutor.BuildExceptionFromErrorResponse(response);

        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = "SomeTrain",
                ExternalId = "abc",
                Input = null,
            }
        );
        metadata.AddException(rebuilt);

        response.IsError.Should().BeTrue();
        metadata.FailureClass.Should().Be(FailureClass.Unclassified);
        metadata.FailureJunction.Should().Be("DoThing");
    }
}
