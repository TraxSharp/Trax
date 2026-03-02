using System.Text.Json;
using FluentAssertions;
using Trax.Effect.Utils;

namespace Trax.Effect.Tests.Integration.UnitTests.Utils;

[TestFixture]
public class DisposableConverterTests
{
    [Test]
    public void CanConvert_DisposableType_ReturnsTrue()
    {
        // Arrange
        var converter = new DisposableConverter();

        // Act
        var result = converter.CanConvert(typeof(MemoryStream));

        // Assert
        result.Should().BeTrue();
    }

    [Test]
    public void CanConvert_NonDisposableType_ReturnsFalse()
    {
        // Arrange
        var converter = new DisposableConverter();

        // Act
        var result = converter.CanConvert(typeof(string));

        // Assert
        result.Should().BeFalse();
    }

    [Test]
    public void Write_DisposableObject_WritesPlaceholder()
    {
        // Arrange
        var options = new JsonSerializerOptions();
        options.Converters.Add(new DisposableConverter());
        using var stream = new MemoryStream();

        // Act
        var json = JsonSerializer.Serialize<object>(stream, options);

        // Assert
        json.Should().Contain("IDisposable");
    }
}
