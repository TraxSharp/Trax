using FluentAssertions;
using LanguageExt;
using Trax.Core.Exceptions;
using Trax.Core.Junction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.Tests.Integration.UnitTests.Services;

/// <summary>
/// A chain declaration cannot read the value being processed.
///
/// <para>A chain that branches on its input has no single shape, so the one read at startup need
/// not be the one that runs, and verifying it would prove nothing. The accessors for
/// per-execution state therefore throw while a chain is being declared, which makes that kind of
/// chain impossible to write rather than merely discouraged.</para>
///
/// <para>Enforces Trax.Docs/adr/0016-a-junction-chain-is-a-declaration-not-a-step-of-the-work.md.</para>
/// </summary>
[Property("adr", "Trax.Docs/adr/0016-a-junction-chain-is-a-declaration-not-a-step-of-the-work.md")]
[TestFixture]
public class ChainDeclarationTests
{
    [Test]
    public void DeclaredChain_WhenTheChainReadsTheInput_Throws()
    {
        var declaring = () => new ReadsItsInputTrain().DeclaredChain();

        declaring
            .Should()
            .Throw<ChainDeclarationException>()
            .Where(e => e.Message.Contains(nameof(ReadsItsInputTrain)))
            .Where(e => e.Message.Contains("TrainInput"))
            .Where(
                e => e.Message.Contains("junction"),
                "the message has to say where the work belongs instead"
            );
    }

    [Test]
    public void DeclaredChain_WhenTheChainReadsTheOutput_Throws()
    {
        var declaring = () => new ReadsItsOutputTrain().DeclaredChain();

        declaring.Should().Throw<ChainDeclarationException>();
    }

    [Test]
    public void DeclaredChain_WhenTheChainOnlyNamesJunctions_Succeeds()
    {
        var steps = new WellBehavedTrain().DeclaredChain().Steps;

        steps.Should().HaveCount(2);
        steps[0].Junction.Should().Be(typeof(PassThrough));
    }

    [Test]
    public void TrainInput_OutsideDeclaration_StillReadsAsBefore()
    {
        var train = new WellBehavedTrain();

        train.ReadInput().Should().BeNull("no metadata is attached, so the input is default");
    }

    private class PassThrough : Junction<string, string>
    {
        public override Task<string> Run(string input) => Task.FromResult(input);
    }

    private class WellBehavedTrain : ServiceTrain<string, string>
    {
        protected override Task<Either<Exception, string>> Junctions() =>
            Chain<PassThrough>().Resolve();

        internal string ReadInput() => TrainInput;
    }

    private class ReadsItsInputTrain : ServiceTrain<string, string>
    {
        protected override Task<Either<Exception, string>> Junctions() =>
            TrainInput.Length > 3 ? Chain<PassThrough>().Resolve() : Chain<PassThrough>().Resolve();
    }

    private class ReadsItsOutputTrain : ServiceTrain<string, string>
    {
        protected override Task<Either<Exception, string>> Junctions()
        {
            _ = TrainOutput;
            return Chain<PassThrough>().Resolve();
        }
    }
}
