using System.Text.Json;
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
        // Arrange
        var junction = new TestJunctionWithJsonException();
        var input = Either<Exception, string>.Right("test input");
        var train = new DummyTrain();

        // Act
        var result = await junction.RailwayJunction(input, train);

        // Assert
        Assert.That(
            result.IsLeft,
            Is.True,
            "Expected the junction to fail and return Left(Exception)"
        );

        var exception = result.Swap().ValueUnsafe();

        // The original message should be preserved (not replaced with TrainExceptionData JSON)
        Assert.That(exception.Message, Does.Contain("\"success\":false"));
        Assert.That(exception.Message, Does.Contain("\"referenceId\":\"reference-me2\""));
    }

    [Test]
    public async Task RailwayJunction_WhenExceptionContainsJson_ExceptionDataAvailable()
    {
        // Arrange
        var junction = new TestJunctionWithJsonException();
        var input = Either<Exception, string>.Right("test input");
        var train = new DummyTrain();

        // Act
        var result = await junction.RailwayJunction(input, train);

        // Assert
        var exception = result.Swap().ValueUnsafe();

        // Structured data should be available via Exception.Data
        var data = exception.Data["TrainExceptionData"] as TrainExceptionData;
        Assert.That(data, Is.Not.Null);
        Assert.That(data!.Junction, Is.EqualTo("TestJunctionWithJsonException"));
        Assert.That(data.Type, Is.EqualTo("InvalidOperationException"));
        Assert.That(data.Message, Does.Contain("\"success\":false"));
    }

    [Test]
    public async Task RailwayJunction_WhenExceptionContainsSpecialCharacters_OriginalMessagePreserved()
    {
        // Arrange
        var junction = new TestJunctionWithSpecialCharacters();
        var input = Either<Exception, string>.Right("test input");
        var train = new DummyTrain();

        // Act
        var result = await junction.RailwayJunction(input, train);

        // Assert
        var exception = result.Swap().ValueUnsafe();

        // The original message should be preserved
        Assert.That(exception.Message, Does.Contain("quotes"));
        Assert.That(exception.Message, Does.Contain("newlines"));
        Assert.That(exception.Message, Does.Contain("backslashes"));
    }

    [Test]
    public async Task RailwayJunction_WhenExceptionContainsSpecialCharacters_ExceptionDataAvailable()
    {
        // Arrange
        var junction = new TestJunctionWithSpecialCharacters();
        var input = Either<Exception, string>.Right("test input");
        var train = new DummyTrain();

        // Act
        var result = await junction.RailwayJunction(input, train);

        // Assert
        var exception = result.Swap().ValueUnsafe();

        // Structured data should be available via Exception.Data
        var data = exception.Data["TrainExceptionData"] as TrainExceptionData;
        Assert.That(data, Is.Not.Null);
        Assert.That(data!.Junction, Is.EqualTo("TestJunctionWithSpecialCharacters"));
        Assert.That(data.Type, Is.EqualTo("InvalidOperationException"));

        // Original message preserved in the data object
        Assert.That(data.Message, Does.Contain("quotes"));
        Assert.That(data.Message, Does.Contain("newlines"));
        Assert.That(data.Message, Does.Contain("backslashes"));
    }

    [Test]
    public async Task RailwayJunction_ExceptionDataMessage_CanBeSerializedToValidJson()
    {
        // Arrange
        var junction = new TestJunctionWithJsonException();
        var input = Either<Exception, string>.Right("test input");
        var train = new DummyTrain();

        // Act
        var result = await junction.RailwayJunction(input, train);

        // Assert — the TrainExceptionData can be serialized to valid JSON
        var exception = result.Swap().ValueUnsafe();
        var data = exception.Data["TrainExceptionData"] as TrainExceptionData;
        Assert.That(data, Is.Not.Null);

        var json = JsonSerializer.Serialize(data);
        Assert.That(
            IsValidJson(json),
            Is.True,
            $"Serialized TrainExceptionData should be valid JSON: {json}"
        );

        // Round-trip: deserialize and verify the original JSON message survives
        var roundTripped = JsonSerializer.Deserialize<TrainExceptionData>(json);
        Assert.That(roundTripped, Is.Not.Null);
        Assert.That(roundTripped!.Message, Does.Contain("\"success\":false"));
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

    /// <summary>
    /// Helper method to check if a string is valid JSON.
    /// </summary>
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
