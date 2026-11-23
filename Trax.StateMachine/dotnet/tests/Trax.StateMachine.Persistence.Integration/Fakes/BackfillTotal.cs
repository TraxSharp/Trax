using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.StateMachine.Features;
using Trax.StateMachine.Gherkin;
using Trax.StateMachine.Persistence.FromIr;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

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
