using System.Text.Json;
using AwesomeAssertions;
using Trax.Effect.Services.Checkpoints;
using Trax.Effect.Utils;

namespace Trax.Effect.Tests.Integration.UnitTests.Utils;

/// <summary>
/// What a stored input or output is written with: a plain tree up to 64 levels deep, no reference
/// metadata, and options nobody can change behind every other caller's back. Rows written while
/// references were preserved still read back.
/// </summary>
[TestFixture]
public class StoredJsonOptionsTests
{
    public record Order
    {
        public string Name { get; init; } = "";
        public List<Line> Lines { get; init; } = [];
    }

    public record Line
    {
        public string Sku { get; init; } = "";
    }

    public record Node
    {
        public Node? Next { get; init; }
    }

    private static Node Nested(int depth)
    {
        var node = new Node();
        for (var i = 0; i < depth; i++)
            node = new Node { Next = node };
        return node;
    }

    private static IEnumerable<TestCaseData> StoredOptions()
    {
        yield return new TestCaseData(TraxJsonSerializationOptions.Default).SetArgDisplayNames(
            "Default"
        );
        yield return new TestCaseData(
            TraxJsonSerializationOptions.ManifestProperties
        ).SetArgDisplayNames("ManifestProperties");
    }

    [TestCaseSource(nameof(StoredOptions))]
    public void The_options_refuse_to_be_changed(JsonSerializerOptions options)
    {
        var act = () => options.MaxDepth = 8;

        act.Should()
            .Throw<InvalidOperationException>(
                "the options are shared process-wide; a caller copies them to change a setting"
            );
    }

    [TestCaseSource(nameof(StoredOptions))]
    public void A_list_is_written_as_an_array_with_no_reference_metadata(
        JsonSerializerOptions options
    )
    {
        var json = JsonSerializer.Serialize(
            new Order { Name = "o", Lines = [new Line { Sku = "a" }] },
            options
        );

        json.Should().NotContain("$id").And.NotContain("$values");
        JsonDocument
            .Parse(json)
            .RootElement.GetProperty("lines")
            .ValueKind.Should()
            .Be(JsonValueKind.Array);
    }

    [TestCaseSource(nameof(StoredOptions))]
    public void An_input_nested_twenty_deep_is_written_and_read_back(JsonSerializerOptions options)
    {
        var input = Nested(20);

        var json = JsonSerializer.Serialize(input, options);

        JsonSerializer
            .Deserialize<Node>(json, options)
            .Should()
            .BeEquivalentTo(input, o => o.AllowingInfiniteRecursion());
    }

    [Test]
    public void A_row_written_while_references_were_preserved_reads_back_as_the_same_input()
    {
        var input = new Order
        {
            Name = "o",
            Lines = [new Line { Sku = "a" }, new Line { Sku = "b" }],
        };
        const string oldRow = """
            {"$id":"1","name":"o","lines":{"$id":"2","$values":[{"$id":"3","sku":"a"},{"$id":"4","sku":"b"}]}}
            """;

        RunInputs
            .Same(oldRow, current: null, input, typeof(Order))
            .Should()
            .BeTrue("a run stored before the upgrade still resumes and replays");
    }

    [Test]
    public void A_plain_row_reads_back_as_the_same_input()
    {
        var input = new Order { Name = "o", Lines = [new Line { Sku = "a" }] };
        var row = JsonSerializer.Serialize(input, TraxJsonSerializationOptions.Default);

        RunInputs.Same(row, current: null, input, typeof(Order)).Should().BeTrue();
        StoredJson.CarriesReferences(row).Should().BeFalse();
    }
}
