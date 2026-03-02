using System.Text.Json;
using FluentAssertions;
using Trax.Core.Exceptions;

namespace Trax.Core.Tests.Unit.UnitTests.Exceptions;

public class TrainExceptionDataTests : TestSetup
{
    [Theory]
    public async Task Serialize_RoundTrip_PreservesAllProperties()
    {
        // Arrange
        var data = new TrainExceptionData
        {
            TrainName = "MyTrain",
            TrainExternalId = "ext-123",
            Type = "InvalidOperationException",
            Step = "ValidateInput",
            Message = "Input was null",
        };

        // Act
        var json = JsonSerializer.Serialize(data);
        var deserialized = JsonSerializer.Deserialize<TrainExceptionData>(json);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.TrainName.Should().Be("MyTrain");
        deserialized.TrainExternalId.Should().Be("ext-123");
        deserialized.Type.Should().Be("InvalidOperationException");
        deserialized.Step.Should().Be("ValidateInput");
        deserialized.Message.Should().Be("Input was null");
    }

    [Theory]
    public async Task Serialize_UsesJsonPropertyNames()
    {
        // Arrange
        var data = new TrainExceptionData
        {
            TrainName = "Test",
            TrainExternalId = "id",
            Type = "Exception",
            Step = "Step",
            Message = "msg",
        };

        // Act
        var json = JsonSerializer.Serialize(data);

        // Assert
        json.Should().Contain("\"trainName\"");
        json.Should().Contain("\"trainExternalId\"");
        json.Should().Contain("\"type\"");
        json.Should().Contain("\"step\"");
        json.Should().Contain("\"message\"");
        // Should NOT contain PascalCase property names
        json.Should().NotContain("\"TrainName\"");
        json.Should().NotContain("\"TrainExternalId\"");
    }

    [Theory]
    public async Task Serialize_SpecialCharactersInMessage_EscapedCorrectly()
    {
        // Arrange
        var data = new TrainExceptionData
        {
            TrainName = "Test",
            TrainExternalId = "id",
            Type = "Exception",
            Step = "Step",
            Message = "Line1\nLine2\tTabbed \"quoted\" <html>",
        };

        // Act
        var json = JsonSerializer.Serialize(data);
        var deserialized = JsonSerializer.Deserialize<TrainExceptionData>(json);

        // Assert — round-trip preserves special characters
        deserialized!.Message.Should().Be("Line1\nLine2\tTabbed \"quoted\" <html>");
    }

    [Theory]
    public async Task Deserialize_ValidJson_CreatesCorrectObject()
    {
        // Arrange
        var json =
            """{"trainName":"HelloTrain","trainExternalId":"abc-456","type":"ArgumentException","step":"ParseStep","message":"bad arg"}""";

        // Act
        var data = JsonSerializer.Deserialize<TrainExceptionData>(json);

        // Assert
        data.Should().NotBeNull();
        data!.TrainName.Should().Be("HelloTrain");
        data.TrainExternalId.Should().Be("abc-456");
        data.Type.Should().Be("ArgumentException");
        data.Step.Should().Be("ParseStep");
        data.Message.Should().Be("bad arg");
    }
}
