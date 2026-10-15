using System.Text.Json.Nodes;
using AwesomeAssertions;

namespace Trax.Effect.StateMachine.Tests.UnitTests;

/// <summary>
/// The client manifest is the file a host exports and <c>trax client</c> reads: the contract between Trax.Api, which
/// writes it, and Trax.Cli, which may not reference Trax.Api. It round-trips, it is written byte-for-byte the same for
/// the same content so a committed copy can be checked, and a reader refuses a format it does not know.
/// </summary>
public class ClientManifestTests
{
    private static ClientManifest Sample() =>
        new()
        {
            Machines =
            [
                new ClientManifestMachine
                {
                    Id = "topic-map",
                    IrHash = new string('a', 64),
                    Ir = new JsonObject { ["id"] = "topic-map", ["version"] = 1 },
                    Requires = new ClientManifestRequirement
                    {
                        RoleSets =
                        [
                            ["recovery-user"],
                        ],
                    },
                    Documents =
                    [
                        new ClientManifestDocument
                        {
                            Id =
                                "trax_machine_topic-map_get_"
                                + new string('a', 64)
                                + new string('b', 16),
                            Operation = "get",
                            Document =
                                "query TopicMapGet($id: UUID!) { machines { topicMap { get(id: $id) { id } } } }",
                        },
                    ],
                },
            ],
            Types = "type Query { a: Int }",
        };

    [Test]
    public void Round_trips_through_its_json()
    {
        var text = ClientManifest.Write(Sample());

        var read = ClientManifest.Read(text);

        ClientManifest.Write(read).Should().Be(text);
        read.Machines.Single().Documents.Single().Operation.Should().Be("get");
        read.Machines.Single().Requires!.RoleSets.Single().Should().Equal("recovery-user");
    }

    [Test]
    public void The_same_content_is_written_to_the_same_bytes()
    {
        var text = ClientManifest.Write(Sample());

        text.Should().Be(ClientManifest.Write(Sample()));
        text.Should().EndWith("}\n").And.NotContain("\r");
        text.Should().StartWith("{\n  \"format\": " + ClientManifest.CurrentFormat + ",");
    }

    [Test]
    public void A_newer_format_is_refused_naming_both()
    {
        var newer = JsonNode.Parse(ClientManifest.Write(Sample()))!.AsObject();
        newer["format"] = ClientManifest.CurrentFormat + 1;

        var read = () => ClientManifest.Read(newer.ToJsonString());

        read.Should()
            .Throw<InvalidOperationException>()
            .WithMessage(
                $"*format {ClientManifest.CurrentFormat + 1}*format {ClientManifest.CurrentFormat}*"
            );
    }

    [Test]
    public void A_file_that_is_not_a_manifest_is_refused()
    {
        var read = () => ClientManifest.Read("[1,2]");

        read.Should().Throw<InvalidOperationException>().WithMessage("*not a client manifest*");
    }

    [Test]
    public void A_manifest_missing_a_required_field_is_refused_with_a_message()
    {
        var missing = JsonNode.Parse(ClientManifest.Write(Sample()))!.AsObject();
        missing["machines"]![0]!.AsObject().Remove("irHash");

        var read = () => ClientManifest.Read(missing.ToJsonString());

        read.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*malformed*irHash*Export it again*");
    }

    [TestCase("""{ "format": 1, "machines": [null] }""")]
    [TestCase(
        """{ "format": 1, "trains": [{ "field": "a", "root": "mutation", "documents": [null] }] }"""
    )]
    [TestCase("""{ "format": 1, "types": null }""")]
    [TestCase("""{ "format": 1, "machines": [{ "id": "m", "irHash": "h", "ir": 3 }] }""")]
    public void A_null_or_a_value_of_another_kind_is_refused_with_a_message(string text)
    {
        var read = () => ClientManifest.Read(text);

        read.Should().Throw<InvalidOperationException>().WithMessage("*malformed*Export it again*");
    }
}
