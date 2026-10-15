using Trax.Samples.StateMachine.E2E.Fixtures;
using Trax.Samples.StateMachine.E2E.Utilities;

namespace Trax.Samples.StateMachine.E2E.Tests;

/// <summary>
/// The host serves only the documents it registered: the page and every test here send a generated id with its
/// variables, and a document the host did not register, a variant of a real machine operation's included, is refused
/// before it runs. The
/// sample does not opt in to the generic <c>stateMachine</c> mutations, so its generated client has none.
/// </summary>
[Property(
    "adr",
    "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md"
)]
[TestFixture]
public class PersistedOnlyTests : StateMachineTestFixture
{
    [Test]
    public async Task AnAdHocDocument_IsRefused()
    {
        // The start document with one field fewer: a document the host did not register. (The registered text itself
        // is the same document as its id, so the host runs it.)
        var start = StateMachineClient.DocumentOf(Turnstile, "start")!;
        var unregistered = start.Document.Replace("dueAtKnown ", "", StringComparison.Ordinal);
        unregistered.Should().NotBe(start.Document);

        var asText = await GraphQL.SendAsync(
            unregistered,
            Alice,
            new { requestId = $"start-{Guid.NewGuid():N}" }
        );
        var introspection = await GraphQL.SendAsync(
            "{ __schema { mutationType { name } } }",
            Alice
        );

        foreach (var response in new[] { asText, introspection })
        {
            response
                .HasErrors.Should()
                .BeTrue($"a document sent as text is refused: {response.Raw}");
            response
                .Root.TryGetProperty("data", out var data)
                .Should()
                .BeFalse($"nothing ran: {data}");
        }
    }

    [Test]
    public async Task The_same_operation_by_its_id_is_served()
    {
        var (id, started) = await Machines.StartAsync(Alice, Turnstile);

        id.Should().NotBeNull(started.Raw);
        started.State.Should().Be("Locked");
    }

    [Test]
    public void The_generated_client_has_both_machines_and_no_generic_mutation()
    {
        StateMachineClient
            .Manifest.Machines.Select(m => m.Id)
            .Should()
            .BeEquivalentTo([Turnstile, Checkout], "the sample exposes both of its machines");
        StateMachineClient
            .Manifest.Trains.Select(t => t.Field)
            .Should()
            .NotContain(
                ["saveSnapshot", "advanceSnapshot", "loadSnapshot", "sendSnapshot"],
                "the sample does not call ExposeGenericStateMachineMutations (Trax.Docs/adr/0048)"
            );
    }
}
