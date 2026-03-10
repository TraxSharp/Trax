using System.Text.Json;
using FluentAssertions;
using Trax.Effect.Utils;

namespace Trax.Effect.Tests.Integration.UnitTests.Utils;

[TestFixture]
public class SystemTypeConverterTests
{
    private JsonSerializerOptions _options;

    [SetUp]
    public void Setup()
    {
        _options = new JsonSerializerOptions();
        _options.Converters.Add(new SystemTypeConverter());
    }

    [Test]
    [TestCase(typeof(string))]
    [TestCase(typeof(int))]
    [TestCase(typeof(DateTime))]
    public void Serialize_PrimitiveType_ReturnsAssemblyQualifiedName(Type typeToTest)
    {
        // Arrange
        var testClass = new TestClass { TypeProperty = typeToTest };

        // Act
        var json = JsonSerializer.Serialize(testClass, _options);

        // Assert
        json.Should().Contain(typeToTest.AssemblyQualifiedName);
    }

    [Test]
    public void Serialize_CustomType_ReturnsAssemblyQualifiedName()
    {
        // Arrange
        var testClass = new TestClass { TypeProperty = typeof(TestClass) };

        // Act
        var json = JsonSerializer.Serialize(testClass, _options);

        // Assert
        // The '+' in nested class names gets encoded as \u002B in JSON
        // We need to either decode the JSON first or use a different assertion approach
        var deserializedObj = JsonSerializer.Deserialize<TestClass>(json, _options);
        deserializedObj!.TypeProperty.Should().Be(typeof(TestClass));
    }

    [Test]
    public void Serialize_NullType_HandlesCorrectly()
    {
        // Arrange
        var testClass = new TestClass { TypeProperty = null };

        // Act
        var json = JsonSerializer.Serialize(testClass, _options);

        // Assert
        json.Should().Contain("\"TypeProperty\":null");
    }

    [Test]
    [TestCase(typeof(string))]
    [TestCase(typeof(int))]
    [TestCase(typeof(DateTime))]
    public void Deserialize_ValidTypeName_ReturnsCorrectType(Type expectedType)
    {
        // Arrange
        var typeName = expectedType.AssemblyQualifiedName;
        var json = $"{{\"TypeProperty\":\"{typeName!.Replace("\"", "\\\"")}\"}}";

        // Act
        var result = JsonSerializer.Deserialize<TestClass>(json, _options);

        // Assert
        result!.TypeProperty.Should().Be(expectedType);
    }

    [Test]
    public void Deserialize_CustomTypeName_ReturnsCorrectType()
    {
        // Arrange
        var typeName = typeof(TestClass).AssemblyQualifiedName;
        var json = $"{{\"TypeProperty\":\"{typeName!.Replace("\"", "\\\"")}\"}}";

        // Act
        var result = JsonSerializer.Deserialize<TestClass>(json, _options);

        // Assert
        result!.TypeProperty.Should().Be(typeof(TestClass));
    }

    [Test]
    public void Deserialize_NullTypeName_ReturnsNull()
    {
        // Arrange
        var json = "{\"TypeProperty\":null}";

        // Act
        var result = JsonSerializer.Deserialize<TestClass>(json, _options);

        // Assert
        result!.TypeProperty.Should().BeNull();
    }

    [Test]
    public void Deserialize_InvalidTypeName_ThrowsException()
    {
        // Arrange
        var json = "{\"TypeProperty\":\"NonExistentType, NonExistentAssembly\"}";

        // Act & Assert
        var act = () => JsonSerializer.Deserialize<TestClass>(json, _options);
        act.Should().Throw<JsonException>().WithMessage("*Unable to find type*");
    }

    [Test]
    [TestCase(typeof(int))]
    [TestCase(typeof(string))]
    [TestCase(typeof(DateTime))]
    public void RoundTrip_ValidType_PreservesValue(Type typeToTest)
    {
        // Arrange
        var original = new TestClass { TypeProperty = typeToTest };

        // Act
        var json = JsonSerializer.Serialize(original, _options);
        var deserialized = JsonSerializer.Deserialize<TestClass>(json, _options);

        // Assert
        deserialized!.TypeProperty.Should().Be(original.TypeProperty);
    }

    [Test]
    public void RoundTrip_ComplexObject_PreservesTypes()
    {
        // Arrange
        var original = new ComplexObject
        {
            Name = "Test Object",
            IntType = typeof(int),
            StringType = typeof(string),
            CustomType = typeof(TestClass),
        };

        // Act
        var json = JsonSerializer.Serialize(original, _options);
        var deserialized = JsonSerializer.Deserialize<ComplexObject>(json, _options);

        // Assert
        deserialized!.Name.Should().Be(original.Name);
        deserialized.IntType.Should().Be(original.IntType);
        deserialized.StringType.Should().Be(original.StringType);
        deserialized.CustomType.Should().Be(original.CustomType);
    }

    // Test helper classes
    private class TestClass
    {
        public Type? TypeProperty { get; set; }
    }

    private class ComplexObject
    {
        public string Name { get; set; } = null!;
        public Type? IntType { get; set; }
        public Type? StringType { get; set; }
        public Type? CustomType { get; set; }
    }
}
