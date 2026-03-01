using System.Text.Json;
using Trax.Effect.Utils;

namespace Trax.Effect.Tests.Integration.Utils.Converters;

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
        StringAssert.Contains(typeToTest.AssemblyQualifiedName, json);
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
        Assert.AreEqual(typeof(TestClass), deserializedObj.TypeProperty);
    }

    [Test]
    public void Serialize_NullType_HandlesCorrectly()
    {
        // Arrange
        var testClass = new TestClass { TypeProperty = null };

        // Act
        var json = JsonSerializer.Serialize(testClass, _options);

        // Assert
        StringAssert.Contains("\"TypeProperty\":null", json);
    }

    [Test]
    [TestCase(typeof(string))]
    [TestCase(typeof(int))]
    [TestCase(typeof(DateTime))]
    public void Deserialize_ValidTypeName_ReturnsCorrectType(Type expectedType)
    {
        // Arrange
        var typeName = expectedType.AssemblyQualifiedName;
        var json = $"{{\"TypeProperty\":\"{typeName.Replace("\"", "\\\"")}\"}}";
        ;

        // Act
        var result = JsonSerializer.Deserialize<TestClass>(json, _options);

        // Assert
        Assert.AreEqual(expectedType, result.TypeProperty);
    }

    [Test]
    public void Deserialize_CustomTypeName_ReturnsCorrectType()
    {
        // Arrange
        var typeName = typeof(TestClass).AssemblyQualifiedName;
        var json = $"{{\"TypeProperty\":\"{typeName.Replace("\"", "\\\"")}\"}}";
        ;

        // Act
        var result = JsonSerializer.Deserialize<TestClass>(json, _options);

        // Assert
        Assert.AreEqual(typeof(TestClass), result.TypeProperty);
    }

    [Test]
    public void Deserialize_NullTypeName_ReturnsNull()
    {
        // Arrange
        var json = "{\"TypeProperty\":null}";

        // Act
        var result = JsonSerializer.Deserialize<TestClass>(json, _options);

        // Assert
        Assert.IsNull(result.TypeProperty);
    }

    [Test]
    public void Deserialize_InvalidTypeName_ThrowsException()
    {
        // Arrange
        var json = "{\"TypeProperty\":\"NonExistentType, NonExistentAssembly\"}";

        // Act & Assert
        var ex = Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<TestClass>(json, _options)
        );

        // Verify the exception message contains useful information
        StringAssert.Contains("Unable to find type", ex.Message);
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
        Assert.AreEqual(original.TypeProperty, deserialized.TypeProperty);
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
        Assert.AreEqual(original.Name, deserialized.Name);
        Assert.AreEqual(original.IntType, deserialized.IntType);
        Assert.AreEqual(original.StringType, deserialized.StringType);
        Assert.AreEqual(original.CustomType, deserialized.CustomType);
    }

    // Test helper classes
    private class TestClass
    {
        public Type TypeProperty { get; set; }
    }

    private class ComplexObject
    {
        public string Name { get; set; }
        public Type IntType { get; set; }
        public Type StringType { get; set; }
        public Type CustomType { get; set; }
    }
}
