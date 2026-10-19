using AwesomeAssertions;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.Configuration;
using Trax.Api.GraphQL.PersistedOperations.Services;

namespace Trax.Api.Tests.PersistedOperations.UnitTests;

/// <summary>
/// A host that serves the operations console registers the console's documents under ids carrying the hash of their
/// text, keeps them to the operations surface and the operator's streams, and leaves out, rather than refusing the
/// start over, a document it cannot serve.
/// <para>Enforces <c>Trax.Api/docs/adr/0042-the-operations-console-is-registered-as-persisted-documents.md</c>.</para>
/// </summary>
[TestFixture]
[Property("adr", "docs/adr/0042-the-operations-console-is-registered-as-persisted-documents.md")]
public class ConsoleDocumentsTests
{
    private const string Adr =
        "docs/adr/0042-the-operations-console-is-registered-as-persisted-documents.md";

    private static ISchemaDefinition _schema = null!;

    [OneTimeSetUp]
    public async Task BuildTheSchema()
    {
        var services = new ServiceCollection();
        services
            .AddGraphQLServer("console")
            .AddType(
                new ObjectType(d =>
                    d.Name("Operations").Field("hosts").Type<StringType>().Resolve("h")
                )
            )
            .AddQueryType(d =>
            {
                d.Name("RootQuery");
                d.Field("operations")
                    .Type(new HotChocolate.Language.NamedTypeNode("Operations"))
                    .Resolve(new object());
                d.Field("persistedOperations").Type<StringType>().Resolve("p");
                d.Field("discover").Type<StringType>().Resolve("d");
            })
            .AddMutationType(d =>
            {
                d.Name("RootMutation");
                d.Field("operations")
                    .Type(new HotChocolate.Language.NamedTypeNode("Operations"))
                    .Resolve(new object());
            })
            .AddSubscriptionType(d =>
            {
                d.Name("RootSubscription");
                d.Field("onTrainStateChanged").Type<StringType>().Resolve("s");
                d.Field("onMachineInstanceChanged").Type<StringType>().Resolve("s");
                d.Field("onSomethingElse").Type<StringType>().Resolve("s");
            });
        var executor = await services
            .BuildServiceProvider()
            .GetRequiredService<IRequestExecutorProvider>()
            .GetExecutorAsync("console");
        _schema = executor.Schema;
    }

    [Test]
    public void A_console_id_is_reserved_and_reads_back_to_its_slot_and_name()
    {
        var id = ConsoleDocuments.IdOf(
            "Dead_Letters",
            "query Dead_Letters { operations { hosts } }"
        );

        id.Should().StartWith("trax_console_Dead-Letters_");
        GeneratedDocuments.IsReserved(id).Should().BeTrue();
        GeneratedDocuments.IsContentAddressed(id).Should().BeTrue();
        GeneratedDocuments.PrefixOf(id).Should().Be(ConsoleDocuments.ReservedIdPrefix);
        ConsoleDocuments.SlotOfId(id).Should().Be("trax_console_Dead-Letters");
        ConsoleDocuments.NameOfId(id).Should().Be("Dead_Letters");
        GeneratedDocuments
            .IsGenerated(id, "query Dead_Letters { operations { hosts } }")
            .Should()
            .BeTrue();
        GeneratedDocuments
            .IsGenerated(id, "query Dead_Letters { operations { other } }")
            .Should()
            .BeFalse();
    }

    [TestCase("query Hosts { operations { hosts } }")]
    [TestCase("mutation Hosts { operations { hosts } }")]
    [TestCase("subscription Hosts { onTrainStateChanged }")]
    [TestCase("subscription Hosts { onMachineInstanceChanged }")]
    [TestCase("query Hosts { operations { ...H } } fragment H on Operations { hosts }")]
    public void A_document_of_the_operations_surface_is_served(string text)
    {
        var (served, skipped) = ConsoleDocuments.Select(
            _schema,
            [new ConsoleDocumentText("Hosts", text)]
        );

        skipped.Should().BeEmpty();
        served.Should().ContainSingle().Which.Id.Should().Be(ConsoleDocuments.IdOf("Hosts", text));
    }

    [TestCase("query Hosts { persistedOperations }", "*other than 'operations'*")]
    [TestCase("query Hosts { discover }", "*other than 'operations'*")]
    [TestCase("query Hosts { operations { hosts } discover }", "*other than 'operations'*")]
    [TestCase("query Hosts { o: operations { hosts } }", "*other than a field*")]
    [TestCase("query Other { operations { hosts } }", "*named 'Other'*")]
    [TestCase("subscription Hosts { onSomethingElse }", "*operator's streams*")]
    [TestCase("query Hosts { operations @include(if: true) { hosts } }", "*directive*")]
    [TestCase(
        "query Hosts { operations { hosts } } query B { operations { hosts } }",
        "*2 operations*"
    )]
    [TestCase("query Hosts { operations { missing } }", "*refused by this host*")]
    public void A_document_the_host_cannot_serve_is_left_out_naming_why(string text, string reason)
    {
        var (served, skipped) = ConsoleDocuments.Select(
            _schema,
            [new ConsoleDocumentText("Hosts", text)]
        );

        served
            .Should()
            .BeEmpty("a console document reaches the operations surface only, per " + Adr);
        skipped.Should().ContainSingle().Which.Should().Match(reason);
    }

    [Test]
    public void Leaving_one_out_keeps_the_rest()
    {
        var (served, skipped) = ConsoleDocuments.Select(
            _schema,
            [
                new ConsoleDocumentText("Hosts", "query Hosts { operations { hosts } }"),
                new ConsoleDocumentText("Gone", "query Gone { operations { missing } }"),
            ]
        );

        served.Select(d => d.Name).Should().Equal("Hosts");
        skipped.Should().ContainSingle().Which.Should().StartWith("'Gone'");
    }
}
