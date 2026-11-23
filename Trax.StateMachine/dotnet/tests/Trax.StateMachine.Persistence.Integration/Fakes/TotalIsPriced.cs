using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.StateMachine.Features;
using Trax.StateMachine.Gherkin;
using Trax.StateMachine.Persistence.FromIr;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

/// <summary>The total is the price of the items.</summary>
public sealed class TotalIsPriced : ICustomRule
{
    public bool Holds(JsonObject context, JsonNode? input) =>
        context["total"] is JsonValue total
        && total.GetValueKind() == JsonValueKind.Number
        && string.Equals(
            total.ToJsonString(),
            (FeatureCheckout.ItemsCount(context) * FeatureCheckout.UnitPrice).ToString(
                CultureInfo.InvariantCulture
            ),
            StringComparison.Ordinal
        );
}
