using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.StateMachine.Features;
using Trax.StateMachine.Gherkin;
using Trax.StateMachine.Persistence.FromIr;
using Trax.StateMachine.Tests.Fakes;
using static Trax.StateMachine.Rules;

namespace Trax.StateMachine.Tests.UnitTests;

/// <summary>A migration that keeps the snapshot as it was, for a machine whose versions differ in nothing stored.</summary>
public sealed class KeepsTheSnapshot : IMachineMigration
{
    public MigrationResult Migrate(string state, JsonObject context) => new(state, context);
}
