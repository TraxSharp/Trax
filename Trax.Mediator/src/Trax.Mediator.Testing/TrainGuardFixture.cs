using System.Reflection;
using NUnit.Framework;
using Trax.Core.Testing;

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
    /// <summary>
    /// The assemblies whose trains are checked: concrete classes deriving
    /// <c>ServiceTrain&lt;,&gt;</c> or implementing <c>IServiceTrain&lt;,&gt;</c>.
    /// </summary>
    protected abstract IReadOnlyList<Assembly> TrainAssemblies { get; }

    [Test]
    public void Every_train_has_a_companion_interface() =>
        AssertCheckedAndClean(TrainGuards.EveryTrainHasInterface(TrainAssemblies));

    /// <summary>
    /// The guard looked at a train, and found nothing wrong. A fixture whose
    /// <see cref="TrainAssemblies"/> hold no train checked nothing, so it fails rather than passes.
    /// </summary>
    internal static void AssertCheckedAndClean(GuardResult result)
    {
        Assert.That(
            result.Inspected,
            Is.GreaterThan(0),
            "The guard found no train in TrainAssemblies, so it checked nothing. Point "
                + "TrainAssemblies at the assemblies that declare your trains."
        );
        Assert.That(result.Offenders, Is.Empty, result.FailureMessage);
    }
}
