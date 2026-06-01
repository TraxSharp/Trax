using System.Reflection;

namespace Trax.Mediator.Testing.Tests;

/// <summary>
/// Runs <see cref="TrainGuardFixture"/> as a consumer would: subclass and point it at an assembly with
/// no trains, so the inherited guard runs to completion. The offender path is covered directly in
/// <see cref="TrainGuardsTests"/>.
/// </summary>
[TestFixture]
public sealed class TrainGuardFixtureSelfTest : TrainGuardFixture
{
    protected override IReadOnlyList<Assembly> TrainAssemblies => [typeof(object).Assembly];
}
