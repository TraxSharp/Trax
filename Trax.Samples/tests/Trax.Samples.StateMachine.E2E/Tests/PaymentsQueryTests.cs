using Trax.Samples.StateMachine.E2E.Fixtures;
using Trax.Samples.StateMachine.E2E.Utilities;

namespace Trax.Samples.StateMachine.E2E.Tests;

/// <summary>
/// <c>payments.listCharges</c> is the page's view of the payment provider: it needs a signed-in caller and
/// answers with that caller's charges only. (This host's factory swaps the charge for a recording fake, so
/// the provider takes nothing here; PaymentTests covers the real charge.)
/// </summary>
[TestFixture]
public class PaymentsQueryTests : StateMachineTestFixture
{
    private static string Id => StateMachineClient.DocumentIdOf("paymentsListCharges");

    [Test]
    public async Task An_anonymous_caller_cannot_list_charges()
    {
        var result = await GraphQL.SendIdAsync(Id, Anonymous, new { });

        result.IsRefused.Should().BeTrue(result.Raw);
    }

    [Test]
    public async Task A_signed_in_caller_lists_their_charges()
    {
        var result = await GraphQL.SendIdAsync(Id, Alice, new { });

        result.HasErrors.Should().BeFalse(result.Raw);
        result
            .GetData("discover", "payments", "listCharges", "charges")
            .ValueKind.Should()
            .Be(System.Text.Json.JsonValueKind.Array);
    }
}
