using AwesomeAssertions;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Core.Monad;
using Trax.Core.Train;

namespace Trax.Core.Tests.Unit.UnitTests.Train;

/// <summary>
/// Every chain call made on the train starts from the train's monad. Two such calls in separate
/// statements are two chains running at once over one Memory unless the body awaits the first,
/// and the declaration cannot show which step reads what the other wrote, so it is refused.
///
/// <para>Enforces Trax.Docs/adr/0016-a-junction-chain-is-a-declaration-not-a-step-of-the-work.md.</para>
/// </summary>
[Property("adr", "Trax.Docs/adr/0016-a-junction-chain-is-a-declaration-not-a-step-of-the-work.md")]
public class UnlinkedChainTests : TestSetup
{
    [Test]
    public void TwoChainsStartedAsSeparateStatements_AreRefused() =>
        new StatementChainsTrain()
            .DeclaredChain()
            .Refusals.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("separate statement");

    [Test]
    public void SynchronousCallsBeforeTheFirstJunction_StillVerify() =>
        new ServicesThenChainTrain().DeclaredChain().Refusals.Should().BeEmpty();

    [Test]
    public void AStatementAfterAnAwaitedChain_StillVerifies() =>
        new AwaitedThenExtractTrain().DeclaredChain().Refusals.Should().BeEmpty();

    [Test]
    public void AStatementAfterAChainAwaitedWithConfigureAwait_StillVerifies() =>
        new ConfigureAwaitThenExtractTrain().DeclaredChain().Refusals.Should().BeEmpty();

    [Test]
    public void AStatementAfterAChainAwaitedAsATask_StillVerifies() =>
        new AsTaskThenExtractTrain().DeclaredChain().Refusals.Should().BeEmpty();

    [Test]
    public void AStatementAfterAChainConvertedToATaskAndAwaited_StillVerifies() =>
        new ConvertedThenExtractTrain().DeclaredChain().Refusals.Should().BeEmpty();

    [Test]
    public void ALinkedChain_StillVerifies() =>
        new LinkedTrain().DeclaredChain().Refusals.Should().BeEmpty();

    public interface IA { }

    public interface IB { }

    private sealed class A : IA { }

    private sealed class B : IB { }

    public sealed class Holder
    {
        public int Inner { get; init; } = 3;
    }

    private sealed class StringToInt : Junction<string, int>
    {
        public override Task<int> Run(string input) => Task.FromResult(input.Length);
    }

    private sealed class IntToBool : Junction<int, bool>
    {
        public override Task<bool> Run(int input) => Task.FromResult(input > 0);
    }

    private sealed class StringToHolder : Junction<string, Holder>
    {
        public override Task<Holder> Run(string input) => Task.FromResult(new Holder());
    }

    private sealed class StatementChainsTrain : Train<string, bool>
    {
        protected override Task<Either<Exception, bool>> Junctions()
        {
            Chain<StringToInt>();
            return Chain<IntToBool>().Resolve();
        }
    }

    private sealed class ServicesThenChainTrain : Train<string, bool>
    {
        protected override Task<Either<Exception, bool>> Junctions()
        {
            AddServices<IA>(new A());
            AddServices<IB>(new B());
            return Chain<StringToInt>().Chain<IntToBool>().Resolve();
        }
    }

    private sealed class AwaitedThenExtractTrain : Train<string, bool>
    {
        protected override async Task<Either<Exception, bool>> Junctions()
        {
            await Chain<StringToHolder>();
            Extract<Holder, int>();
            return await Chain<IntToBool>().Resolve();
        }
    }

    private sealed class LinkedTrain : Train<string, bool>
    {
        protected override Task<Either<Exception, bool>> Junctions() =>
            Chain<StringToInt>().Chain<IntToBool>().Resolve();
    }

    private sealed class ConfigureAwaitThenExtractTrain : Train<string, bool>
    {
        protected override async Task<Either<Exception, bool>> Junctions()
        {
            await Chain<StringToHolder>().ConfigureAwait(false);
            Extract<Holder, int>();
            return await Chain<IntToBool>().Resolve();
        }
    }

    private sealed class AsTaskThenExtractTrain : Train<string, bool>
    {
        protected override async Task<Either<Exception, bool>> Junctions()
        {
            await Chain<StringToHolder>().AsTask();
            Extract<Holder, int>();
            return await Chain<IntToBool>().Resolve();
        }
    }

    private sealed class ConvertedThenExtractTrain : Train<string, bool>
    {
        protected override async Task<Either<Exception, bool>> Junctions()
        {
            Task<Monad<string, bool>> first = Chain<StringToHolder>();
            await first;
            Extract<Holder, int>();
            return await Chain<IntToBool>().Resolve();
        }
    }
}
