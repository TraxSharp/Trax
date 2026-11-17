using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.StateMachine.Features;
using Trax.StateMachine.Gherkin;
using Trax.StateMachine.Persistence.FromIr;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

/// <summary>Appends the item and prices the cart.</summary>
public sealed class AddPricedItem : ICustomReduction
{
    public JsonObject Reduce(JsonObject context, JsonNode? input)
    {
        var next = (JsonObject)context.DeepClone();
        var items = next["items"] as JsonArray ?? [];
        items.Add(input!["item"]!.GetValue<string>());
        next["items"] = items;
        next["total"] = items.Count * FeatureCheckout.UnitPrice;
        return next;
    }
}
