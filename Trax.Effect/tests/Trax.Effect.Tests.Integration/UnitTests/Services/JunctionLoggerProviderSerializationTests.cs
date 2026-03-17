using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Trax.Effect.Utils;

namespace Trax.Effect.Tests.Integration.UnitTests.Services;

[TestFixture]
public class JunctionLoggerProviderSerializationTests
{
    private JsonSerializerOptions _options = null!;

    [SetUp]
    public void SetUp()
    {
        _options = new JsonSerializerOptions
        {
            WriteIndented = false,
            IncludeFields = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            ReferenceHandler = ReferenceHandler.IgnoreCycles,
            MaxDepth = 8,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter(), new DisposableConverter() },
        };
    }

    #region SimplePoco

    [Test]
    public void Serialize_SimpleObject_ProducesValidJson()
    {
        var obj = new SimplePoco
        {
            Name = "test",
            Count = 42,
            Active = true,
        };

        var json = JsonSerializer.Serialize<object>(obj, _options);

        json.Should().Contain("\"name\":\"test\"");
        json.Should().Contain("\"count\":42");
        json.Should().Contain("\"active\":true");
    }

    #endregion

    #region CircularReferences

    [Test]
    public void Serialize_ObjectWithCircularReference_DoesNotThrow()
    {
        var parent = new CircularParent { Name = "parent" };
        var child = new CircularChild { Name = "child", Parent = parent };
        parent.Child = child;

        var act = () => JsonSerializer.Serialize<object>(parent, _options);

        act.Should().NotThrow();
    }

    [Test]
    public void Serialize_ObjectWithCircularReference_WritesNullForCycle()
    {
        var parent = new CircularParent { Name = "parent" };
        var child = new CircularChild { Name = "child", Parent = parent };
        parent.Child = child;

        var json = JsonSerializer.Serialize<object>(parent, _options);

        json.Should().Contain("\"name\":\"parent\"");
        json.Should().Contain("\"name\":\"child\"");
        // IgnoreCycles writes null for the back-reference, and WhenWritingNull omits it
        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("child")
            .TryGetProperty("parent", out var parentRef)
            .Should()
            .BeFalse();
    }

    [Test]
    public void Serialize_ObjectWithSelfReference_HandlesIgnoreCycles()
    {
        var self = new SelfReferencing { Name = "loop" };
        self.Self = self;

        var act = () => JsonSerializer.Serialize<object>(self, _options);

        act.Should().NotThrow();
        var json = JsonSerializer.Serialize<object>(self, _options);
        var doc = JsonDocument.Parse(json);
        // IgnoreCycles writes null, WhenWritingNull omits it entirely
        doc.RootElement.TryGetProperty("self", out _).Should().BeFalse();
    }

    #endregion

    #region IDisposable

    [Test]
    public void Serialize_ObjectWithIDisposableMember_WritesPlaceholder()
    {
        using var stream = new MemoryStream();
        var obj = new ObjectWithDisposable { Name = "test", Resource = stream };

        var json = JsonSerializer.Serialize<object>(obj, _options);

        json.Should().Contain("\"name\":\"test\"");
        json.Should().Contain("\"resource\":\"[ IDisposable ]\"");
    }

    #endregion

    #region Enums

    [Test]
    public void Serialize_ObjectWithEnum_WritesEnumAsString()
    {
        var obj = new ObjectWithEnum { Status = TestStatus.Active };

        var json = JsonSerializer.Serialize<object>(obj, _options);

        json.Should().Contain("\"status\":\"Active\"");
        json.Should().NotContain("1");
    }

    #endregion

    #region NestedObjects

    [Test]
    public void Serialize_ObjectWithNestedObjects_SerializesCorrectly()
    {
        var obj = new OuterObject
        {
            Id = "outer",
            Inner = new InnerObject { Value = 99 },
        };

        var json = JsonSerializer.Serialize<object>(obj, _options);

        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("id").GetString().Should().Be("outer");
        doc.RootElement.GetProperty("inner").GetProperty("value").GetInt32().Should().Be(99);
    }

    #endregion

    #region NullHandling

    [Test]
    public void Serialize_ObjectWithNullProperties_OmitsNulls()
    {
        var obj = new SimplePoco { Name = "test", NullableField = null };

        var json = JsonSerializer.Serialize<object>(obj, _options);

        json.Should().Contain("\"name\":\"test\"");
        json.Should().NotContain("nullableField");
    }

    [Test]
    public void Serialize_NullObject_ReturnsNullLiteral()
    {
        var json = JsonSerializer.Serialize<object?>(null, _options);

        json.Should().Be("null");
    }

    #endregion

    #region TestModels

    private class SimplePoco
    {
        public string Name { get; init; } = "";
        public int Count { get; init; }
        public bool Active { get; init; }
        public string? NullableField { get; init; }
    }

    private class CircularParent
    {
        public string Name { get; init; } = "";
        public CircularChild? Child { get; set; }
    }

    private class CircularChild
    {
        public string Name { get; init; } = "";
        public CircularParent? Parent { get; set; }
    }

    private class SelfReferencing
    {
        public string Name { get; init; } = "";
        public SelfReferencing? Self { get; set; }
    }

    private class ObjectWithDisposable
    {
        public string Name { get; init; } = "";
        public MemoryStream? Resource { get; init; }
    }

    private enum TestStatus
    {
        Inactive,
        Active,
    }

    private class ObjectWithEnum
    {
        public TestStatus Status { get; init; }
    }

    private class OuterObject
    {
        public string Id { get; init; } = "";
        public InnerObject? Inner { get; init; }
    }

    private class InnerObject
    {
        public int Value { get; init; }
    }

    #endregion
}
