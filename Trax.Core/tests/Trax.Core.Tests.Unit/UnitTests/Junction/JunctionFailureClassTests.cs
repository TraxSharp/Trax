using System.Text.Json;
using AwesomeAssertions;
using Trax.Core.Exceptions;
using Trax.Core.Junction;
using Trax.Core.Tests.Unit.Utils;

namespace Trax.Core.Tests.Unit.UnitTests.Junction;

/// <summary>
/// A junction that fails attaches fresh exception data, and a classification the failure already
/// carried has to survive that. A remote run's worker classifies where it holds the real
/// exception, and the calling side only ever sees the result through a junction.
///
/// <para>Enforces Trax.Docs/adr/0020-a-failure-is-classified-where-it-happens-and-carried.md.</para>
/// </summary>
[Property("adr", "Trax.Docs/adr/0020-a-failure-is-classified-where-it-happens-and-carried.md")]
public class JunctionFailureClassTests : TestSetup
{
    [Test]
    public async Task AClassCarriedInTheMessage_SurvivesTheJunction()
    {
        var remote = new TrainException(
            JsonSerializer.Serialize(
                new TrainExceptionData
                {
                    TrainName = "",
                    TrainExternalId = "",
                    Type = "DbUpdateConcurrencyException",
                    Junction = "SaveOrder",
                    Message = "row changed",
                    FailureClass = FailureClass.Conflict,
                }
            )
        );

        var data = await DataAfterFailing(remote);

        data.FailureClass.Should().Be(FailureClass.Conflict);
    }

    [Test]
    public async Task AClassAlreadyAttached_SurvivesTheJunction()
    {
        var nested = new InvalidOperationException("inner");
        nested.Data["TrainExceptionData"] = new TrainExceptionData
        {
            TrainName = "Inner",
            TrainExternalId = "",
            Type = nameof(InvalidOperationException),
            Junction = "InnerJunction",
            Message = "inner",
            FailureClass = FailureClass.Transient,
        };

        var data = await DataAfterFailing(nested);

        data.FailureClass.Should().Be(FailureClass.Transient);
    }

    [Test]
    public async Task AnUnclassifiedFailure_CarriesNoClass()
    {
        var data = await DataAfterFailing(new InvalidOperationException("{not json"));

        data.FailureClass.Should()
            .BeNull("a message that merely starts with a brace is not a record");
    }

    [Test]
    public async Task AClassInTheMessage_IsCarriedOnlyByATrainException()
    {
        var data = await DataAfterFailing(
            new InvalidOperationException(RecordJson(((int)FailureClass.Permanent).ToString()))
        );

        data.FailureClass.Should()
            .BeNull("only a TrainException carries a failure class in its message");
    }

    [Test]
    public async Task AClassInTheMessage_IsNotReadFromATypeDerivedFromTrainException()
    {
        // A consumer's own exception type deriving from TrainException carries whatever text it
        // was built with, often a remote system's response body. Trax never rebuilds a recorded
        // failure as a derived type, so its message is not a record.
        var data = await DataAfterFailing(
            new UpstreamError(RecordJson(((int)FailureClass.Transient).ToString()))
        );

        data.FailureClass.Should()
            .BeNull(
                "only a TrainException that Trax rebuilt from a record carries a class in its message"
            );
    }

    private sealed class UpstreamError(string body) : TrainException(body);

    [Test]
    public async Task AnUndefinedClassInTheMessage_IsCarriedAsUnclassified()
    {
        var data = await DataAfterFailing(new TrainException(RecordJson("42")));

        data.FailureClass.Should().Be(FailureClass.Unclassified);
    }

    [Test]
    public async Task AnUndefinedClassAlreadyAttached_IsCarriedAsUnclassified()
    {
        var nested = new InvalidOperationException("inner");
        nested.Data["TrainExceptionData"] = new TrainExceptionData
        {
            TrainName = "Inner",
            TrainExternalId = "",
            Type = nameof(InvalidOperationException),
            Junction = "InnerJunction",
            Message = "inner",
            FailureClass = (FailureClass)42,
        };

        var data = await DataAfterFailing(nested);

        data.FailureClass.Should().Be(FailureClass.Unclassified);
    }

    private static string RecordJson(string failureClass) =>
        "{\"trainName\":\"a\",\"trainExternalId\":\"b\",\"type\":\"X\","
        + "\"junction\":\"J\",\"message\":\"m\",\"failureClass\":"
        + failureClass
        + "}";

    private static async Task<TrainExceptionData> DataAfterFailing(Exception failure)
    {
        var result = await new Throwing(failure).RailwayJunction(1, UnitTrain.Create());

        result.IsLeft.Should().BeTrue();
        return (TrainExceptionData)result.Swap().ValueUnsafe().Data["TrainExceptionData"]!;
    }

    private class Throwing(Exception failure) : Junction<int, int>
    {
        public override Task<int> Run(int input) => throw failure;
    }
}
