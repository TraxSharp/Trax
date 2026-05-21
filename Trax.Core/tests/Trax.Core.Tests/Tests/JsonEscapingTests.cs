using System.Text.Json;
using FluentAssertions;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using NUnit.Framework;
using Trax.Core.Exceptions;
using Trax.Core.Junction;
using Trax.Core.Train;

namespace Trax.Core.Tests.Tests;

/// <summary>
/// Tests to verify that exception messages with special characters (including JSON content)
/// are preserved in their original form, and that structured junction context is available
/// via Exception.Data["TrainExceptionData"].
/// </summary>
public class JsonEscapingTests
{
    private class DummyTrain : Train<string, string>
    {
        protected override Task<Either<Exception, string>> RunInternal(string input) =>
            Task.FromResult(Either<Exception, string>.Right(input));
    }

    /// <summary>
    /// Test junction that throws an exception with JSON content in the message.
    /// This simulates the scenario where an API exception contains JSON in its message.
    /// </summary>
    private class TestJunctionWithJsonException : Junction<string, string>
    {
        public override Task<string> Run(string input)
        {
            var jsonMessage =
                """{"success":false,"referenceId":"reference-me2","amount":null,"id":"7551812047776009403814","submitTimeUtc":null,"cardType":null,"metadata":{"cybersourceReason":"Decline - Insufficient funds in the account.","statusReason":"The credit card was declined with a reason.","processorReason":"Decline - Insufficient funds in the account.","cardVerificationReason":null,"addressVerificationReason":null},"attemptCount":2}""";

            throw new InvalidOperationException(jsonMessage);
        }
    }

    [Test]
    public async Task RailwayJunction_WhenExceptionContainsJson_OriginalMessagePreserved()
    {
        var junction = new TestJunctionWithJsonException();
        var input = Either<Exception, string>.Right("test input");
        var train = new DummyTrain();

        var result = await junction.RailwayJunction(input, train);

        result.IsLeft.Should().BeTrue("the junction should fail and return Left(Exception)");

        var exception = result.Swap().ValueUnsafe();

        // The original message should be preserved (not replaced with TrainExceptionData JSON)
        exception.Message.Should().Contain("\"success\":false");
        exception.Message.Should().Contain("\"referenceId\":\"reference-me2\"");
    }

    [Test]
    public async Task RailwayJunction_WhenExceptionContainsJson_ExceptionDataAvailable()
    {
        var junction = new TestJunctionWithJsonException();
        var input = Either<Exception, string>.Right("test input");
        var train = new DummyTrain();

        var result = await junction.RailwayJunction(input, train);

        var exception = result.Swap().ValueUnsafe();

        var data = exception.Data["TrainExceptionData"] as TrainExceptionData;
        data.Should().NotBeNull();
        data!.Junction.Should().Be("TestJunctionWithJsonException");
        data.Type.Should().Be("InvalidOperationException");
        data.Message.Should().Contain("\"success\":false");
    }

    [Test]
    public async Task RailwayJunction_WhenExceptionContainsSpecialCharacters_OriginalMessagePreserved()
    {
        var junction = new TestJunctionWithSpecialCharacters();
        var input = Either<Exception, string>.Right("test input");
        var train = new DummyTrain();

        var result = await junction.RailwayJunction(input, train);

        var exception = result.Swap().ValueUnsafe();

        exception.Message.Should().Contain("quotes");
        exception.Message.Should().Contain("newlines");
        exception.Message.Should().Contain("backslashes");
    }

    [Test]
    public async Task RailwayJunction_WhenExceptionContainsSpecialCharacters_ExceptionDataAvailable()
    {
        var junction = new TestJunctionWithSpecialCharacters();
        var input = Either<Exception, string>.Right("test input");
        var train = new DummyTrain();

        var result = await junction.RailwayJunction(input, train);

        var exception = result.Swap().ValueUnsafe();

        var data = exception.Data["TrainExceptionData"] as TrainExceptionData;
        data.Should().NotBeNull();
        data!.Junction.Should().Be("TestJunctionWithSpecialCharacters");
        data.Type.Should().Be("InvalidOperationException");

        data.Message.Should().Contain("quotes");
        data.Message.Should().Contain("newlines");
        data.Message.Should().Contain("backslashes");
    }

    [Test]
    public async Task RailwayJunction_ExceptionDataMessage_CanBeSerializedToValidJson()
    {
        var junction = new TestJunctionWithJsonException();
        var input = Either<Exception, string>.Right("test input");
        var train = new DummyTrain();

        var result = await junction.RailwayJunction(input, train);

        var exception = result.Swap().ValueUnsafe();
        var data = exception.Data["TrainExceptionData"] as TrainExceptionData;
        data.Should().NotBeNull();

        var json = JsonSerializer.Serialize(data);
        IsValidJson(json)
            .Should()
            .BeTrue($"serialized TrainExceptionData should be valid JSON: {json}");

        var roundTripped = JsonSerializer.Deserialize<TrainExceptionData>(json);
        roundTripped.Should().NotBeNull();
        roundTripped!.Message.Should().Contain("\"success\":false");
    }

    /// <summary>
    /// Test junction that throws an exception with special characters that need JSON escaping.
    /// </summary>
    private class TestJunctionWithSpecialCharacters : Junction<string, string>
    {
        public override Task<string> Run(string input)
        {
            var messageWithSpecialChars =
                "This message contains \"quotes\", \nnewlines, and \\backslashes that need escaping.";
            throw new InvalidOperationException(messageWithSpecialChars);
        }
    }

    private static bool IsValidJson(string jsonString)
    {
        try
        {
            JsonDocument.Parse(jsonString);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
