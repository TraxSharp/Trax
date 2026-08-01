using FluentAssertions;
using Trax.Effect.StateMachine.Testing;
using Trax.Effect.StateMachine.Tests.Fakes;
using Trax.Effect.StateMachine.Tests.Helpers;

namespace Trax.Effect.StateMachine.Tests.Differential;

/// <summary>
/// Replays the shared, TypeScript-generated differential corpus through the C# engine. TypeScript is the
/// oracle (it enumerates + records outcomes into machines/&lt;m&gt;/differential.json); this proves the C#
/// engine reproduces every case, which is the exhaustive cross-runtime parity guarantee (PD1). To change
/// behavior, regenerate the corpus on the TypeScript side (UPDATE_DIFFERENTIAL=1) — this test then holds
/// C# to the new oracle. Only reason codes + canonical wire are compared, never rejection detail (PD7).
/// </summary>
public class DifferentialConformanceTests
{
    [Test]
    public void Turnstile_engine_reproduces_the_shared_differential_corpus() =>
        AssertReplays("turnstile", TestTurnstile.Machine);

    [Test]
    public void Checkout_engine_reproduces_the_shared_differential_corpus() =>
        AssertReplays("checkout", TestCheckout.Machine);

    private static void AssertReplays<TState, TTrigger>(
        string name,
        SnapshotMachine<TState, TTrigger> machine
    )
        where TState : struct, Enum
        where TTrigger : struct, Enum
    {
        var file = FixturePaths.DifferentialFile(name);
        if (file is null || !File.Exists(file))
        {
            Assert.Ignore(
                $"Shared {name} differential corpus not found (isolated build). In CI it is supplied "
                    + "by the Trax.StateMachine.Fixtures package."
            );
            return;
        }

        var diffs = DifferentialCorpus.Replay(machine, File.ReadAllText(file));
        diffs
            .Should()
            .BeEmpty(
                "the C# engine must reproduce the TypeScript oracle's differential corpus exactly:\n"
                    + string.Join("\n", diffs)
            );
    }
}
