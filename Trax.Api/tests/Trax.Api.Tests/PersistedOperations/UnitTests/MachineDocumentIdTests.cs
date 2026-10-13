using AwesomeAssertions;
using Trax.Api.GraphQL.PersistedOperations.Services;

namespace Trax.Api.Tests.PersistedOperations.UnitTests;

/// <summary>
/// A machine document's id reads back to exactly the machine that generated it: the machine is the segment between
/// the reserved prefix and the next <c>_</c>, so a machine whose id begins with another's is never counted as it, and
/// an operation that holds <c>_</c> does not move the boundary. A machine id that holds <c>_</c> would make the
/// boundary ambiguous, so no id is generated for one.
/// <para>Enforces <c>Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md</c>.</para>
/// </summary>
[TestFixture]
[Property("adr", Adr)]
public class MachineDocumentIdTests
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    private static readonly string Hash = new('a', MachineDocuments.IrHashLength);

    [Test]
    public void An_id_names_only_the_machine_whose_id_is_its_whole_machine_segment()
    {
        var order = MachineDocuments.IdOf("order", "get", Hash, "query { }");
        var orderV2 = MachineDocuments.IdOf("order-v2", "get", Hash, "query { }");

        MachineDocuments.MachineOf(order, ["order", "order-v2"]).Should().Be("order");
        MachineDocuments.MachineOf(orderV2, ["order", "order-v2"]).Should().Be("order-v2");
        MachineDocuments.MachineOf(orderV2, ["order"]).Should().BeNull();
        MachineDocuments.MachineOf(order, ["order-v2"]).Should().BeNull();
    }

    [Test]
    public void An_operation_holding_an_underscore_leaves_the_id_with_its_own_machine()
    {
        // `order`'s event `V2_Start` is served as the operation `v2_Start`.
        var id = MachineDocuments.IdOf("order", "v2_Start", Hash, "mutation { }");

        MachineDocuments.MachineOf(id, ["order", "order_v2"]).Should().Be("order");
        MachineDocuments.MachineOf(id, ["order_v2"]).Should().BeNull();
    }

    [Test]
    public void No_id_is_generated_for_a_machine_id_holding_an_underscore()
    {
        var act = () => MachineDocuments.IdOf("order_v2", "get", Hash, "query { }");

        act.Should().Throw<ArgumentException>().WithMessage("*order_v2*'_'*");
    }

    [Test]
    public void An_id_outside_the_prefix_or_without_a_machine_segment_names_no_machine()
    {
        MachineDocuments.MachineOf("order_get_x", ["order"]).Should().BeNull();
        MachineDocuments
            .MachineOf($"{MachineDocuments.ReservedIdPrefix}_get_x", ["", "order"])
            .Should()
            .BeNull();
        MachineDocuments
            .MachineOf($"{MachineDocuments.ReservedIdPrefix}order", ["order"])
            .Should()
            .BeNull();
    }
}
