using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.StateMachine.Features;
using Trax.StateMachine.Gherkin;
using Trax.StateMachine.Persistence.FromIr;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

/// <summary>
/// A checkout written as features with the C# a host binds to it (ADR <c>Trax.Docs/adr/0058</c>): a run-once charge
/// on Pay, a custom reduction that prices the cart, a custom rule every state requires, and a migration from version 1,
/// which had no total.
/// </summary>
public static class FeatureCheckout
{
    public const int UnitPrice = 999;

    private static readonly string Namespace = typeof(FeatureCheckout).Namespace!;

    private static readonly string Glossary = $$"""
        {
          "version": 1,
          "actors": { "shopper": { "noun": "a shopper", "anyAuthenticated": true } },
          "machines": {
            "feature-checkout": {
              "ownedBy": "shopper",
              "noun": "checkout",
              "start": "has a checkout",
              "version": 2,
              "initial": "Cart",
              "context": [
                { "name": "items", "type": "array", "nullable": false, "constraints": [], "values": { "type": "string", "nullable": false } },
                { "name": "receipt", "type": "string", "nullable": true, "constraints": [] },
                { "name": "total", "type": "integer", "nullable": false, "constraints": [] }
              ],
              "states": {
                "Cart": {
                  "phrase": "is in the cart",
                  "requires": { "rule": "all", "rules": [
                    { "rule": "absent", "source": "context", "field": "receipt" },
                    { "rule": "custom", "name": "priced" } ] }
                },
                "Review": { "phrase": "is in review", "requires": { "rule": "custom", "name": "priced" } },
                "Paid": {
                  "phrase": "is paid",
                  "committed": true,
                  "requires": { "rule": "all", "rules": [
                    { "rule": "nonEmpty", "source": "context", "field": "receipt" },
                    { "rule": "custom", "name": "priced" } ] }
                }
              },
              "triggers": {
                "AddItem": {
                  "phrase": "adds {item} to {subject}",
                  "input": [ { "name": "item", "type": "string", "nullable": false, "constraints": [] } ],
                  "reduce": { "reduce": "custom", "name": "add-item" }
                },
                "Next": { "phrase": "reviews {subject}" },
                "Pay": {
                  "phrase": "pays for {subject}",
                  "input": [ { "name": "receipt", "type": "string", "nullable": false, "constraints": [] } ],
                  "reduce": { "reduce": "set", "steps": [ { "field": "receipt", "value": { "input": "receipt" } } ] },
                  "effect": { "type": "{{Namespace}}.IFeatureCharge", "example": "rcpt-example" }
                }
              },
              "rules": { "priced": { "type": "{{Namespace}}.TotalIsPriced", "example": true } },
              "reductions": {
                "add-item": { "type": "{{Namespace}}.AddPricedItem", "example": { "items": ["bread"], "total": {{UnitPrice}} } }
              },
              "migrations": { "1": "{{Namespace}}.BackfillTotal" }
            }
          }
        }
        """;

    private const string Features = """
        Feature: Checking out
          Scenario: Paying for what is in the cart
            Given Ada is a shopper
            And Ada has a checkout
            When Ada adds "bread" to her checkout
            Then Ada's checkout is in the cart
            When Ada reviews her checkout
            Then Ada's checkout is in review
            When Ada pays for her checkout
            Then Ada's checkout is paid
        """;

    /// <summary>The derived machine, served from its IR and host entry, with its C# found in this assembly.</summary>
    public static IMachine Served()
    {
        var derived = FeatureMachines.Derive(
            FeatureGlossary.Read(Glossary),
            [new FeatureFile("checkout.feature", Features)]
        );
        derived.Succeeded.Should().BeTrue(string.Join("\n", derived.Problems));
        var ir = JsonNode.Parse(derived.Irs["feature-checkout"])!.AsObject();
        return IrMachines.Create(
            new IrMachines.Source(ir, HostFile.Read(derived.Host!).Machines["feature-checkout"]),
            [typeof(FeatureCheckout).Assembly]
        );
    }

    internal static long ItemsCount(JsonObject context) =>
        context["items"] is JsonArray items ? items.Count : 0;
}

/// <summary>The checkout's charge.</summary>
public interface IFeatureCharge : ISnapshotEffect;

/// <summary>A charge that counts its calls and returns a receipt per call.</summary>
public sealed class CountingCharge : IFeatureCharge
{
    private int _calls;

    public int Calls => Volatile.Read(ref _calls);

    public Task<string> Run(Snapshot snapshot, CancellationToken cancellationToken = default) =>
        Task.FromResult($"charge-{Interlocked.Increment(ref _calls)}");
}

/// <summary>The total is the price of the items.</summary>
public sealed class TotalIsPriced : ICustomRule
{
    public bool Holds(JsonObject context, JsonNode? input) =>
        context["total"] is JsonValue total
        && total.GetValueKind() == JsonValueKind.Number
        && total.ToJsonString()
            == (FeatureCheckout.ItemsCount(context) * FeatureCheckout.UnitPrice).ToString(
                CultureInfo.InvariantCulture
            );
}

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

/// <summary>Version 1 had no total: price the items.</summary>
public sealed class BackfillTotal : IMachineMigration
{
    public MigrationResult Migrate(string state, JsonObject context)
    {
        var next = (JsonObject)context.DeepClone();
        next["total"] = FeatureCheckout.ItemsCount(context) * FeatureCheckout.UnitPrice;
        return new MigrationResult(state, next);
    }
}
