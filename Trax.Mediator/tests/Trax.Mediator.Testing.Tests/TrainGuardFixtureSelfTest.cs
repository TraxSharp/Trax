using System.Reflection;
using NUnit.Framework.Internal;

namespace Trax.Mediator.Testing.Tests;

/// <summary>
/// Runs <see cref="TrainGuardFixture"/> as a consumer would: subclass it and point it at an assembly
/// whose trains all have their interface, so the inherited guard runs to completion.
/// </summary>
[TestFixture]
public sealed class TrainGuardFixtureSelfTest : TrainGuardFixture
{
    protected override IReadOnlyList<Assembly> TrainAssemblies => [typeof(GoodTrain).Assembly];
}
