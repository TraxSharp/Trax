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
            Junction = "ValidateInput",
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
        deserialized.Junction.Should().Be("ValidateInput");
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
            Junction = "Junction",
            Message = "msg",
        };

        // Act
        var json = JsonSerializer.Serialize(data);

        // Assert
        json.Should().Contain("\"trainName\"");
        json.Should().Contain("\"trainExternalId\"");
        json.Should().Contain("\"type\"");
        json.Should().Contain("\"junction\"");
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
            Junction = "Junction",
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
            """{"trainName":"HelloTrain","trainExternalId":"abc-456","type":"ArgumentException","junction":"ParseJunction","message":"bad arg"}""";

        // Act
        var data = JsonSerializer.Deserialize<TrainExceptionData>(json);

        // Assert
        data.Should().NotBeNull();
        data!.TrainName.Should().Be("HelloTrain");
        data.TrainExternalId.Should().Be("abc-456");
        data.Type.Should().Be("ArgumentException");
        data.Junction.Should().Be("ParseJunction");
        data.Message.Should().Be("bad arg");
    }

    #region StackTrace Serialization

    [Theory]
    public async Task Serialize_RoundTrip_PreservesStackTrace()
    {
        // Arrange
        var data = new TrainExceptionData
        {
            TrainName = "MyTrain",
            TrainExternalId = "ext-123",
            Type = "InvalidOperationException",
            Junction = "ValidateInput",
            Message = "Input was null",
            StackTrace = "   at MyApp.ValidateInput.Run() in /app/Validate.cs:line 42",
        };

        // Act
        var json = JsonSerializer.Serialize(data);
        var deserialized = JsonSerializer.Deserialize<TrainExceptionData>(json);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.StackTrace.Should().Be(data.StackTrace);
    }

    [Theory]
    public async Task Serialize_NullStackTrace_HandledCorrectly()
    {
        // Arrange
        var data = new TrainExceptionData
        {
            TrainName = "MyTrain",
            TrainExternalId = "ext-123",
            Type = "Exception",
            Junction = "Junction",
            Message = "msg",
            StackTrace = null,
        };

        // Act
        var json = JsonSerializer.Serialize(data);
        var deserialized = JsonSerializer.Deserialize<TrainExceptionData>(json);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.StackTrace.Should().BeNull();
    }

    [Theory]
    public async Task Deserialize_WithoutStackTraceField_BackwardsCompatible()
    {
        // Arrange — JSON from an older version that doesn't include stackTrace
        var json =
            """{"trainName":"Test","trainExternalId":"id","type":"Exception","junction":"J","message":"msg"}""";

        // Act
        var data = JsonSerializer.Deserialize<TrainExceptionData>(json);

        // Assert — should deserialize successfully with null StackTrace
        data.Should().NotBeNull();
        data!.StackTrace.Should().BeNull();
        data.Message.Should().Be("msg");
    }

    [Theory]
    public async Task Serialize_StackTraceWithSpecialCharacters_PreservedCorrectly()
    {
        // Arrange
        var data = new TrainExceptionData
        {
            TrainName = "Test",
            TrainExternalId = "id",
            Type = "Exception",
            Junction = "J",
            Message = "msg",
            StackTrace =
                "   at MyApp.Run() in C:\\Users\\dev\\src\\App.cs:line 10\n   at System.Threading.Tasks.Task.Execute()",
        };

        // Act
        var json = JsonSerializer.Serialize(data);
        var deserialized = JsonSerializer.Deserialize<TrainExceptionData>(json);

        // Assert
        deserialized!.StackTrace.Should().Be(data.StackTrace);
    }

    #endregion
}
