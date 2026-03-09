using System.Text.Json;
using FluentAssertions;
using Trax.Api.GraphQL.Types;

namespace Trax.Api.Tests;

[TestFixture]
public class JsonElementConverterTests
{
    #region Null and Empty

    [Test]
    public void ToObject_NullJson_ThrowsArgumentNullException()
    {
        var act = () => JsonElementConverter.ToObject(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void ToObject_EmptyObject_ReturnsEmptyDictionary()
    {
        var result = JsonElementConverter.ToObject("{}");

        result.Should().BeOfType<Dictionary<string, object?>>();
        var dict = (Dictionary<string, object?>)result!;
        dict.Should().BeEmpty();
    }

    [Test]
    public void ToObject_EmptyArray_ReturnsEmptyList()
    {
        var result = JsonElementConverter.ToObject("[]");

        result.Should().BeOfType<List<object?>>();
        var list = (List<object?>)result!;
        list.Should().BeEmpty();
    }

    #endregion

    #region Primitive Values

    [Test]
    public void ToObject_StringValue_ReturnsString()
    {
        var result = JsonElementConverter.ToObject("\"hello\"");
        result.Should().Be("hello");
    }

    [Test]
    public void ToObject_EmptyString_ReturnsEmptyString()
    {
        var result = JsonElementConverter.ToObject("\"\"");
        result.Should().Be("");
    }

    [Test]
    public void ToObject_IntegerValue_ReturnsLong()
    {
        var result = JsonElementConverter.ToObject("42");
        result.Should().BeOfType<long>();
        result.Should().Be(42L);
    }

    [Test]
    public void ToObject_NegativeInteger_ReturnsLong()
    {
        var result = JsonElementConverter.ToObject("-100");
        result.Should().BeOfType<long>();
        result.Should().Be(-100L);
    }

    [Test]
    public void ToObject_Zero_ReturnsLong()
    {
        var result = JsonElementConverter.ToObject("0");
        result.Should().BeOfType<long>();
        result.Should().Be(0L);
    }

    [Test]
    public void ToObject_FloatingPoint_ReturnsDouble()
    {
        var result = JsonElementConverter.ToObject("3.14");
        result.Should().BeOfType<double>();
        result.Should().Be(3.14);
    }

    [Test]
    public void ToObject_NegativeFloat_ReturnsDouble()
    {
        var result = JsonElementConverter.ToObject("-0.5");
        result.Should().BeOfType<double>();
        result.Should().Be(-0.5);
    }

    [Test]
    public void ToObject_LargeInteger_ReturnsLong()
    {
        var result = JsonElementConverter.ToObject("9223372036854775807");
        result.Should().BeOfType<long>();
        result.Should().Be(long.MaxValue);
    }

    [Test]
    public void ToObject_IntegerExceedingLongMax_ReturnsDouble()
    {
        // 2^63 exceeds long.MaxValue, so TryGetInt64 fails and we fall back to double
        var result = JsonElementConverter.ToObject("9223372036854775808");
        result.Should().BeOfType<double>();
    }

    [Test]
    public void ToObject_True_ReturnsBoolTrue()
    {
        var result = JsonElementConverter.ToObject("true");
        result.Should().BeOfType<bool>();
        result.Should().Be(true);
    }

    [Test]
    public void ToObject_False_ReturnsBoolFalse()
    {
        var result = JsonElementConverter.ToObject("false");
        result.Should().BeOfType<bool>();
        result.Should().Be(false);
    }

    [Test]
    public void ToObject_Null_ReturnsNull()
    {
        var result = JsonElementConverter.ToObject("null");
        result.Should().BeNull();
    }

    #endregion

    #region Simple Objects

    [Test]
    public void ToObject_FlatObject_ReturnsDictionaryWithCorrectTypes()
    {
        var json = """{"name":"Alice","age":30,"active":true}""";
        var result = JsonElementConverter.ToObject(json);

        result.Should().BeOfType<Dictionary<string, object?>>();
        var dict = (Dictionary<string, object?>)result!;
        dict["name"].Should().Be("Alice");
        dict["age"].Should().Be(30L);
        dict["active"].Should().Be(true);
    }

    [Test]
    public void ToObject_ObjectWithNullValue_ReturnsNullEntry()
    {
        var json = """{"key":null}""";
        var result = JsonElementConverter.ToObject(json);

        var dict = (Dictionary<string, object?>)result!;
        dict.Should().ContainKey("key");
        dict["key"].Should().BeNull();
    }

    [Test]
    public void ToObject_ObjectWithMixedTypes_ReturnsCorrectTypes()
    {
        var json = """{"str":"hello","int":1,"float":1.5,"bool":false,"nil":null}""";
        var result = JsonElementConverter.ToObject(json);

        var dict = (Dictionary<string, object?>)result!;
        dict["str"].Should().BeOfType<string>().And.Be("hello");
        dict["int"].Should().BeOfType<long>().And.Be(1L);
        dict["float"].Should().BeOfType<double>().And.Be(1.5);
        dict["bool"].Should().BeOfType<bool>().And.Be(false);
        dict["nil"].Should().BeNull();
    }

    #endregion

    #region Arrays

    [Test]
    public void ToObject_ArrayOfStrings_ReturnsListOfStrings()
    {
        var json = """["a","b","c"]""";
        var result = JsonElementConverter.ToObject(json);

        var list = (List<object?>)result!;
        list.Should().HaveCount(3);
        list.Should().ContainInOrder("a", "b", "c");
    }

    [Test]
    public void ToObject_ArrayOfIntegers_ReturnsListOfLongs()
    {
        var json = "[1,2,3]";
        var result = JsonElementConverter.ToObject(json);

        var list = (List<object?>)result!;
        list.Should().HaveCount(3);
        list.Should().ContainInOrder(1L, 2L, 3L);
    }

    [Test]
    public void ToObject_ArrayOfMixedTypes_ReturnsListWithCorrectTypes()
    {
        var json = """["text",42,3.14,true,null]""";
        var result = JsonElementConverter.ToObject(json);

        var list = (List<object?>)result!;
        list.Should().HaveCount(5);
        list[0].Should().Be("text");
        list[1].Should().Be(42L);
        list[2].Should().Be(3.14);
        list[3].Should().Be(true);
        list[4].Should().BeNull();
    }

    #endregion

    #region Nested Structures

    [Test]
    public void ToObject_NestedObject_ReturnsDictionaryOfDictionaries()
    {
        var json = """{"outer":{"inner":"value"}}""";
        var result = JsonElementConverter.ToObject(json);

        var dict = (Dictionary<string, object?>)result!;
        dict["outer"].Should().BeOfType<Dictionary<string, object?>>();
        var inner = (Dictionary<string, object?>)dict["outer"]!;
        inner["inner"].Should().Be("value");
    }

    [Test]
    public void ToObject_ObjectContainingArray_ReturnsDictionaryWithList()
    {
        var json = """{"tags":["violence","spam","hate-speech"]}""";
        var result = JsonElementConverter.ToObject(json);

        var dict = (Dictionary<string, object?>)result!;
        dict["tags"].Should().BeOfType<List<object?>>();
        var tags = (List<object?>)dict["tags"]!;
        tags.Should().ContainInOrder("violence", "spam", "hate-speech");
    }

    [Test]
    public void ToObject_ArrayOfObjects_ReturnsListOfDictionaries()
    {
        var json = """[{"id":1,"name":"Alice"},{"id":2,"name":"Bob"}]""";
        var result = JsonElementConverter.ToObject(json);

        var list = (List<object?>)result!;
        list.Should().HaveCount(2);

        var first = (Dictionary<string, object?>)list[0]!;
        first["id"].Should().Be(1L);
        first["name"].Should().Be("Alice");

        var second = (Dictionary<string, object?>)list[1]!;
        second["id"].Should().Be(2L);
        second["name"].Should().Be("Bob");
    }

    [Test]
    public void ToObject_DeeplyNested_HandlesMultipleLevels()
    {
        var json = """{"level1":{"level2":{"level3":{"value":"deep"}}}}""";
        var result = JsonElementConverter.ToObject(json);

        var l1 = (Dictionary<string, object?>)result!;
        var l2 = (Dictionary<string, object?>)l1["level1"]!;
        var l3 = (Dictionary<string, object?>)l2["level2"]!;
        var l4 = (Dictionary<string, object?>)l3["level3"]!;
        l4["value"].Should().Be("deep");
    }

    [Test]
    public void ToObject_NestedArraysOfArrays_HandlesCorrectly()
    {
        var json = "[[1,2],[3,4]]";
        var result = JsonElementConverter.ToObject(json);

        var outer = (List<object?>)result!;
        outer.Should().HaveCount(2);

        var first = (List<object?>)outer[0]!;
        first.Should().ContainInOrder(1L, 2L);

        var second = (List<object?>)outer[1]!;
        second.Should().ContainInOrder(3L, 4L);
    }

    #endregion

    #region Realistic Train Outputs

    [Test]
    public void ToObject_ContentModerationOutput_ParsesCorrectly()
    {
        var json = """
            {
                "totalReviewed": 1247,
                "totalFlagged": 83,
                "topViolationTypes": ["violence", "spam", "hate-speech"],
                "falsePositiveRate": 0.042
            }
            """;

        var result = JsonElementConverter.ToObject(json);

        var dict = (Dictionary<string, object?>)result!;
        dict["totalReviewed"].Should().Be(1247L);
        dict["totalFlagged"].Should().Be(83L);
        dict["falsePositiveRate"].Should().Be(0.042);
        var violations = (List<object?>)dict["topViolationTypes"]!;
        violations.Should().ContainInOrder("violence", "spam", "hate-speech");
    }

    [Test]
    public void ToObject_ViolationNoticeOutput_ParsesCorrectly()
    {
        var json = """
            {
                "noticeId": "notice-abc123",
                "deliveredAt": "2026-03-08T19:45:27.771Z",
                "recipientId": "user-456"
            }
            """;

        var result = JsonElementConverter.ToObject(json);

        var dict = (Dictionary<string, object?>)result!;
        dict["noticeId"].Should().Be("notice-abc123");
        dict["deliveredAt"].Should().Be("2026-03-08T19:45:27.771Z");
        dict["recipientId"].Should().Be("user-456");
    }

    [Test]
    public void ToObject_ComplexNestedOutput_ParsesCorrectly()
    {
        var json = """
            {
                "combatResult": {
                    "winner": "player-1",
                    "rounds": [
                        {"round": 1, "damage": 45, "critical": false},
                        {"round": 2, "damage": 120, "critical": true}
                    ],
                    "loot": {
                        "gold": 500,
                        "items": ["sword", "shield"]
                    }
                },
                "duration": 3.5
            }
            """;

        var result = JsonElementConverter.ToObject(json);

        var dict = (Dictionary<string, object?>)result!;
        dict["duration"].Should().Be(3.5);

        var combat = (Dictionary<string, object?>)dict["combatResult"]!;
        combat["winner"].Should().Be("player-1");

        var rounds = (List<object?>)combat["rounds"]!;
        rounds.Should().HaveCount(2);

        var round1 = (Dictionary<string, object?>)rounds[0]!;
        round1["round"].Should().Be(1L);
        round1["damage"].Should().Be(45L);
        round1["critical"].Should().Be(false);

        var round2 = (Dictionary<string, object?>)rounds[1]!;
        round2["critical"].Should().Be(true);

        var loot = (Dictionary<string, object?>)combat["loot"]!;
        loot["gold"].Should().Be(500L);
        var items = (List<object?>)loot["items"]!;
        items.Should().ContainInOrder("sword", "shield");
    }

    #endregion

    #region Edge Cases

    [Test]
    public void ToObject_UnicodeStrings_PreservesCorrectly()
    {
        var json = """{"emoji":"🚂","japanese":"新幹線"}""";
        var result = JsonElementConverter.ToObject(json);

        var dict = (Dictionary<string, object?>)result!;
        dict["emoji"].Should().Be("🚂");
        dict["japanese"].Should().Be("新幹線");
    }

    [Test]
    public void ToObject_EscapedStrings_PreservesCorrectly()
    {
        var json = """{"path":"C:\\Users\\test","quote":"He said \"hi\""}""";
        var result = JsonElementConverter.ToObject(json);

        var dict = (Dictionary<string, object?>)result!;
        dict["path"].Should().Be("C:\\Users\\test");
        dict["quote"].Should().Be("He said \"hi\"");
    }

    [Test]
    public void ToObject_VeryLargeObject_HandlesWithoutError()
    {
        // Build an object with 1000 keys
        var entries = Enumerable.Range(0, 1000).Select(i => $"\"key{i}\":{i}");
        var json = "{" + string.Join(",", entries) + "}";

        var result = JsonElementConverter.ToObject(json);

        var dict = (Dictionary<string, object?>)result!;
        dict.Should().HaveCount(1000);
        dict["key0"].Should().Be(0L);
        dict["key999"].Should().Be(999L);
    }

    [Test]
    public void ToObject_ScientificNotation_ReturnsDouble()
    {
        var json = """{"value":1.5e10}""";
        var result = JsonElementConverter.ToObject(json);

        var dict = (Dictionary<string, object?>)result!;
        dict["value"].Should().BeOfType<double>();
        dict["value"].Should().Be(1.5e10);
    }

    [Test]
    public void ToObject_DuplicateKeys_ThrowsArgumentException()
    {
        // System.Text.Json parses duplicate keys but ToDictionary rejects them
        var json = """{"key":"first","key":"second"}""";

        var act = () => JsonElementConverter.ToObject(json);
        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void ToObject_InvalidJson_ThrowsJsonException()
    {
        var act = () => JsonElementConverter.ToObject("{invalid}");
        act.Should().Throw<JsonException>();
    }

    [Test]
    public void ToObject_PreservesKeyOrder()
    {
        var json = """{"z":"last","a":"first","m":"middle"}""";
        var result = JsonElementConverter.ToObject(json);

        var dict = (Dictionary<string, object?>)result!;
        dict.Keys.Should().ContainInOrder("z", "a", "m");
    }

    #endregion
}
