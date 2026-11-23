using System.Reflection;
using NUnit.Framework.Internal;

namespace Trax.Mediator.Testing.Tests;

/// <summary>
/// The assertion the fixture makes, over the results that must fail it.
/// </summary>
[TestFixture]
public class TrainGuardFixtureAssertionTests
{
    [Test]
    public void Fails_when_the_assemblies_hold_no_train()
    {
        var result = TrainGuards.EveryTrainHasInterface([typeof(object).Assembly]);

        AssertFails(result, "*found no train in TrainAssemblies*");
    }

    [Test]
    public void Fails_on_a_train_without_its_interface()
    {
        var result = TrainGuards.EveryTrainHasInterface([
            typeof(GoodTrain).Assembly,
            BadTrainAssembly.Instance,
        ]);

        AssertFails(result, "*BadTrain*");
    }

    private static void AssertFails(Trax.Core.Testing.GuardResult result, string message)
    {
        // An isolated context, so the fixture's failed assertion is caught here rather than
        // recorded as this test's own failure.
        using (new TestExecutionContext.IsolatedContext())
        {
            var act = () => TrainGuardFixture.AssertCheckedAndClean(result);

            act.Should().Throw<AssertionException>().WithMessage(message);
        }
    }
}
