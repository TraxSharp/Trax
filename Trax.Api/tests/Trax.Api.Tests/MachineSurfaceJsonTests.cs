using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.Api.GraphQL.TypeModules;

namespace Trax.Api.Tests;

/// <summary>
/// The typed surface writes a machine's input record as the JSON the engine validates against the record's schema,
/// which leaves out a computed property: reading never sets one, so the schema has no field for it.
/// </summary>
[TestFixture]
public class MachineSurfaceJsonTests
{
    public sealed record OrderLine
    {
        public int Units { get; init; }
        public int Doubled => Units * 2;
    }

    public sealed record OrderInput(int Qty, int Price)
    {
        public int Total => Qty * Price;
        public OrderLine Line { get; init; } = new();
    }

    [Test]
    public void A_computed_property_is_not_written_into_a_machine_input()
    {
        var json = JsonSerializer
            .SerializeToNode(
                new OrderInput(3, 4) { Line = new OrderLine { Units = 5 } },
                MachineSurface.Json
            )!
            .AsObject();

        json.Select(kv => kv.Key)
            .Should()
            .BeEquivalentTo(["qty", "price", "line"], "the input's schema has no field for Total");
        json["line"]!.AsObject().Select(kv => kv.Key).Should().BeEquivalentTo(["units"]);
        JsonSerializer
            .Deserialize<OrderInput>(json.ToJsonString(), MachineSurface.Json)!
            .Total.Should()
            .Be(12, "a record written without its computed property reads back the same");
    }
}
