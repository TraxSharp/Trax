using System.Text.Json;
using Trax.Samples.StateMachine.E2E.Fixtures;

namespace Trax.Samples.StateMachine.E2E.Tests;

/// <summary>
/// The sample serves its two machines through their own typed operations and does not opt in to the four
/// generic <c>stateMachine</c> mutations, so the schema has none of them and each machine is a field of the
/// <c>machines</c> root.
/// </summary>
[Property(
    "adr",
    "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md"
)]
[TestFixture]
public class GenericMutationsOptInTests : StateMachineTestFixture
{
    [Test]
    public async Task The_StateMachine_sample_runs_without_the_opt_in()
    {
        var mutation = await MutationTypeName();
        var mutationFields = await FieldsOf(mutation);

        mutationFields
            .Should()
            .Contain("machines", "each exposed machine is served under the machines root");
        // The sample has no mutation train, so with the generic mutations off there may be no dispatch at all.
        if (mutationFields.Contains("dispatch"))
            (await FieldsOf(await FieldTypeName(mutation, "dispatch")))
                .Should()
                .NotContain(
                    "stateMachine",
                    "the sample does not call ExposeGenericStateMachineMutations, so the generic "
                        + "mutations are not served (Trax.Docs/adr/0048)"
                );
        (await FieldsOf(await FieldTypeName(mutation, "machines")))
            .Should()
            .Contain([Turnstile, Checkout], "the sample exposes both of its machines");
    }

    private async Task<string> MutationTypeName()
    {
        var response = await GraphQL.SendAsync("{ __schema { mutationType { name } } }", Alice);
        response.HasErrors.Should().BeFalse(response.Raw);
        return response.GetData("__schema", "mutationType", "name").GetString()!;
    }

    private async Task<string> FieldTypeName(string type, string field)
    {
        var response = await GraphQL.SendAsync(
            $$"""{ __type(name: "{{type}}") { fields { name type { name ofType { name } } } } }""",
            Alice
        );
        response.HasErrors.Should().BeFalse(response.Raw);
        var match = response
            .GetData("__type", "fields")
            .EnumerateArray()
            .Single(f => f.GetProperty("name").GetString() == field)
            .GetProperty("type");
        return match.GetProperty("name").GetString()
            ?? match.GetProperty("ofType").GetProperty("name").GetString()!;
    }

    private async Task<List<string>> FieldsOf(string type)
    {
        var response = await GraphQL.SendAsync(
            $$"""{ __type(name: "{{type}}") { fields { name } } }""",
            Alice
        );
        response.HasErrors.Should().BeFalse(response.Raw);
        var fields = response.GetData("__type", "fields");
        return fields.ValueKind == JsonValueKind.Array
            ? fields.EnumerateArray().Select(f => f.GetProperty("name").GetString()!).ToList()
            : [];
    }
}
