using FluentAssertions;
using Trax.Effect.StateMachine.Testing;
using Trax.Effect.StateMachine.Tests.Fakes;
using Trax.Effect.StateMachine.Tests.Helpers;

namespace Trax.Effect.StateMachine.Tests.Differential;

/// <summary>
/// Proves the declarative authoring surface is behaviourally transparent: a turnstile authored with context
/// schemas + <see cref="Rule"/> guards + <see cref="Reduction"/> reducers reproduces the shared differential
/// corpus (produced from the hand-written machine) byte-for-byte, and its declarative model is captured for
/// the IR export.
/// </summary>
public class DeclarativeTurnstileTests
{
    [Test]
    public void Declarative_turnstile_reproduces_the_shared_differential_corpus()
    {
        var file = FixturePaths.DifferentialFile("turnstile");
        if (file is null || !File.Exists(file))
        {
            Assert.Ignore("Shared turnstile differential corpus not found (isolated build).");
            return;
        }

        var diffs = DifferentialCorpus.Replay(DeclarativeTurnstile.Machine, File.ReadAllText(file));

        diffs
            .Should()
            .BeEmpty(
                "a declaratively-authored turnstile must reproduce the corpus exactly:\n"
                    + string.Join("\n", diffs)
            );
    }

    [Test]
    public void The_declarative_model_is_captured_for_export()
    {
        var declarative = DeclarativeTurnstile.Built.Declarative;

        declarative.Should().NotBeNull();
        declarative!.ContextSchemas.Should().ContainKey(TurnstileState.Unlocked);
        declarative
            .ContextSchemas[TurnstileState.Unlocked]
            .Fields.Should()
            .ContainSingle(f =>
                f.Name == "paidWith" && f.Type == JsonFieldType.String && !f.Nullable
            );
        declarative
            .Transitions.Should()
            .Contain(t =>
                t.From == TurnstileState.Locked
                && t.Trigger == TurnstileTrigger.Coin
                && t.Guard is Rule.OneOf
            );
    }
}
