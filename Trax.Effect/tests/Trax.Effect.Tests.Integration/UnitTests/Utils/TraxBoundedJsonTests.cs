using System.Text.Json;
using FluentAssertions;
using Trax.Effect.Utils;

namespace Trax.Effect.Tests.Integration.UnitTests.Utils;

public class TraxBoundedJsonTests
{
    private static readonly JsonSerializerOptions Options = new();

    [Test]
    public void Under_the_ceiling_the_value_is_serialized()
    {
        TraxBoundedJson.Serialize(new[] { 1, 2, 3 }, Options, 64).Should().Be("[1,2,3]");
    }

    [Test]
    public void Over_the_ceiling_the_placeholder_is_returned()
    {
        TraxBoundedJson
            .Serialize(Enumerable.Range(0, 1_000).ToArray(), Options, 64)
            .Should()
            .Be(TraxBoundedJson.TruncatedPlaceholder(64))
            .And.Be("""{"_truncated": true, "_maxBytes": 64}""");
    }

    [Test]
    public void Without_a_ceiling_the_value_is_serialized_whole()
    {
        TraxBoundedJson
            .Serialize(Enumerable.Range(0, 1_000).ToArray(), Options, null)
            .Should()
            .StartWith("[0,1,2");
    }

    [Test]
    public void A_boxed_value_serializes_as_its_runtime_type()
    {
        object value = new Derived { Name = "a", Extra = "b" };

        TraxBoundedJson.Serialize(value, Options, 1024).Should().Contain("\"Extra\":\"b\"");
    }

    private class Base
    {
        public string Name { get; set; } = "";
    }

    private sealed class Derived : Base
    {
        public string Extra { get; set; } = "";
    }
}
