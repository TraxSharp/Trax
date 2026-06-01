using System.Reflection;
using NUnit.Framework;

// The [Test] method name is the documentation; an XML doc comment on it would be pure redundancy.
#pragma warning disable CS1591

namespace Trax.Mediator.Testing;

/// <summary>
/// Pre-written train guard. A consumer subclasses this, supplies the assemblies that contain its
/// trains, and runs <c>dotnet test</c>. No test bodies to write.
/// </summary>
/// <remarks>
/// Example:
/// <code>
/// [TestFixture]
/// public sealed class MyTrainGuards : TrainGuardFixture
/// {
///     protected override IReadOnlyList&lt;Assembly&gt; TrainAssemblies => [typeof(MyAssemblyMarker).Assembly];
/// }
/// </code>
/// </remarks>
[TestFixture]
public abstract class TrainGuardFixture
{
    /// <summary>The assemblies whose <c>ServiceTrain&lt;,&gt;</c> types are checked.</summary>
    protected abstract IReadOnlyList<Assembly> TrainAssemblies { get; }

    [Test]
    public void Every_train_has_a_companion_interface()
    {
        var result = TrainGuards.EveryTrainHasInterface(TrainAssemblies);
        Assert.That(result.Offenders, Is.Empty, result.FailureMessage);
    }
}
