using System.Text.Json;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using NUnit.Framework;
using Trax.Core.Exceptions;
using Trax.Core.Junction;
using Trax.Core.Train;

namespace Trax.Core.Tests.Tests;

/// <summary>
/// Tests to verify that JSON content in exception messages is properly escaped
/// when exceptions are enriched with junction information.
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
    /// This simulates the scenario described in the issue where a CybersourcePaymentsException
    /// contains JSON in its message.
    /// </summary>
    private class TestJunctionWithJsonException : Junction<string, string>
    {
        public override Task<string> Run(string input)
        {
            // Simulate an exception with JSON content in the message (like CybersourcePaymentsException)
            var jsonMessage =
                """{"success":false,"referenceId":"reference-me2","amount":null,"id":"7551812047776009403814","submitTimeUtc":null,"cardType":null,"metadata":{"cybersourceReason":"Decline - Insufficient funds in the account.","statusReason":"The credit card was declined with a reason.","processorReason":"Decline - Insufficient funds in the account.","cardVerificationReason":null,"addressVerificationReason":null},"attemptCount":2}""";

            throw new InvalidOperationException(jsonMessage);
        }
    }

    [Test]
    public async Task RailwayJunction_WhenExceptionContainsJson_ShouldProduceValidJson()
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
        var exceptionMessage = exception.Message;

        // Verify that the exception message is valid JSON
        Assert.That(
            IsValidJson(exceptionMessage),
            Is.True,
            $"Exception message should be valid JSON, but got: {exceptionMessage}"
        );

        // Verify that we can deserialize the exception message
        var exceptionData = JsonSerializer.Deserialize<TrainExceptionData>(exceptionMessage);
        Assert.That(exceptionData, Is.Not.Null);
        Assert.That(exceptionData.Junction, Is.EqualTo("TestJunctionWithJsonException"));
        Assert.That(exceptionData.Type, Is.EqualTo("InvalidOperationException"));

        // Verify that the original JSON message is properly escaped within the message property
        Assert.That(exceptionData.Message, Does.Contain("\"success\":false"));
        Assert.That(exceptionData.Message, Does.Contain("\"referenceId\":\"reference-me2\""));
    }

    [Test]
    public async Task RailwayJunction_WhenExceptionContainsSpecialCharacters_ShouldProduceValidJson()
    {
        // Arrange
        var junction = new TestJunctionWithSpecialCharacters();
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
        var exceptionMessage = exception.Message;

        // Verify that the exception message is valid JSON
        Assert.That(
            IsValidJson(exceptionMessage),
            Is.True,
            $"Exception message should be valid JSON, but got: {exceptionMessage}"
        );

        // Verify that we can deserialize the exception message
        var exceptionData = JsonSerializer.Deserialize<TrainExceptionData>(exceptionMessage);
        Assert.That(exceptionData, Is.Not.Null);
        Assert.That(exceptionData.Junction, Is.EqualTo("TestJunctionWithSpecialCharacters"));
        Assert.That(exceptionData.Type, Is.EqualTo("InvalidOperationException"));

        // Verify that special characters are properly escaped
        Assert.That(exceptionData.Message, Does.Contain("quotes"));
        Assert.That(exceptionData.Message, Does.Contain("newlines"));
        Assert.That(exceptionData.Message, Does.Contain("backslashes"));
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
