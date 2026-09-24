using System.Reflection;
using FluentAssertions;
using Trax.Core.Monad;
using Trax.Core.Train;

namespace Trax.Mediator.Tests.Postgres.Integration.IntegrationTests;

/// <summary>
/// Every train in this assembly declares a chain a host would accept.
/// </summary>
/// <remarks>
/// The contract tests pin what RUN and QUEUE do using trains of their own. A train whose
/// <c>Junctions()</c> awaits real work is not a train a consumer could deploy: <c>DeclaredChain</c>
/// refuses it, so the host refuses to start. Pinning behaviour with a shape nobody can ship means
/// the contract is pinned somewhere the product does not reach.
/// <para>
/// This guard exists to stop new ones appearing. The entries below are the ones that already did;
/// each needs a reason, and the right fix is to reshape the train rather than to lengthen the list.
/// </para>
/// </remarks>
[TestFixture]
public class TestTrainsAreDeployableTests
{
    /// <summary>
    /// Trains this assembly declares that a host would refuse, keyed <c>Fixture.Train</c>, with why
    /// each is tolerated for now.
    /// </summary>
    /// <remarks>
    /// All of these end <c>Junctions()</c> by returning a result rather than by chaining a junction
    /// and calling <c>Resolve()</c>, so there is no chain to verify and the startup check refuses
    /// them. They pin enqueue-side behaviour, where the chain never runs, which is why nobody
    /// noticed. The fix is to give each one a junction that produces its result; it is deliberately
    /// not done here, because these are the trains the RUN/QUEUE and subject-key contracts are
    /// asserted against and reshaping them changes what those tests exercise.
    /// <para>
    /// Keyed per train rather than per fixture on purpose: tolerating a whole fixture would let a
    /// new undeployable train in beside an old one.
    /// </para>
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, string> Tolerated = new Dictionary<
        string,
        string
    >(StringComparer.Ordinal)
    {
        ["DeferredPromotionTests.PlainTrain"] =
            "returns a result; pins deferral, never runs a chain",
        ["DeferredPromotionTests.ImmediateHookTrain"] = "returns a result; pins hook ordering",
        ["DeferredPromotionTests.DeferringThrowTrain"] = "returns a result; pins a throwing hook",
        ["SubjectKeyTests.UnkeyedTrain"] = "returns a result; pins subject-key absence",
        ["SubjectKeyTests.KeyedTrain"] = "returns a result; pins subject-key serialization",
        ["SubjectKeyTests.ThrowingKeyTrain"] = "returns a result; pins a throwing QueueSubjectKey",
        ["SubjectKeyTests.NullKeyTrain"] = "returns a result; pins a null subject key",
        // A different shape, and the one the original finding named: these await real work inside
        // Junctions(), which DeclaredChain refuses outright. The wait is how each test holds a run
        // open at a known point, so reshaping means moving it into a junction.
        ["CancellationContractTests.CooperativeTrain"] =
            "awaits in Junctions() to be cancellable at a known point",
        ["CancellationContractTests.IndifferentTrain"] =
            "awaits in Junctions() to ignore cancellation at a known point",
        ["ExecutionModeContractTests.GatedDownstreamTrain"] =
            "awaits in Junctions() to hold RUN open while QUEUE is observed",
    };

    [Test]
    public void EveryTrainInThisAssembly_DeclaresAChainAHostWouldAccept()
    {
        var offenders = new List<string>();

        foreach (var (type, input, output) in TrainTypes())
        {
            var outer = type.DeclaringType?.Name ?? type.Name;
            if (Tolerated.ContainsKey($"{outer}.{type.Name}"))
                continue;

            object train;
            try
            {
                train = Activator.CreateInstance(type, nonPublic: true)!;
            }
            catch
            {
                // Needs constructor arguments, so it is not something this guard can judge.
                continue;
            }

            var declaredChain = type.GetMethod(
                nameof(Core.Train.Train<,>.DeclaredChain),
                Type.EmptyTypes
            );

            if (declaredChain is null)
                continue;

            ChainRecorder chain;
            try
            {
                chain = (ChainRecorder)declaredChain.Invoke(train, null)!;
            }
            catch (Exception ex)
            {
                offenders.Add($"{outer}.{type.Name}: {(ex.InnerException ?? ex).Message}");
                continue;
            }

            // A refusal is recorded on the recorder rather than thrown, so reading the chain is not
            // enough on its own: Verify is what surfaces it, and it is what the host runs.
            var faults = ChainVerification.Verify(chain, input, output);

            foreach (var fault in faults)
                offenders.Add($"{outer}.{type.Name}: {fault.Reason}");
        }

        offenders
            .Should()
            .BeEmpty(
                "a train whose chain a host would refuse cannot pin the behaviour of a train a "
                    + "host would run. Reshape it, or add it to Tolerated with a reason.\n  "
                    + string.Join("\n  ", offenders)
            );
    }

    /// <summary>Every concrete train in this assembly, with the input and output it declares.</summary>
    private static IEnumerable<(Type Type, Type Input, Type Output)> TrainTypes() =>
        typeof(TestTrainsAreDeployableTests)
            .Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsGenericTypeDefinition: false })
            .Select(t => (Type: t, Base: TrainBase(t)))
            .Where(entry => entry.Base is not null)
            .Select(entry =>
                (
                    entry.Type,
                    entry.Base!.GetGenericArguments()[0],
                    entry.Base!.GetGenericArguments()[1]
                )
            );

    private static Type? TrainBase(Type type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(Train<,>))
                return current;

        return null;
    }
}
