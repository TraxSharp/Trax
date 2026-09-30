using FluentAssertions;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using Trax.Core.Junction;
using Trax.Core.Monad;
using Trax.Core.Train;

namespace Trax.Core.Tests.Unit.UnitTests.Train;

/// <summary>
/// <c>AddServices&lt;T1, T2&gt;(a, b)</c> stores <c>a</c> under <c>T1</c> and <c>b</c> under
/// <c>T2</c>. The chain check records each type argument as a slot, so the run has to fill
/// exactly those slots, whichever other interfaces the objects happen to implement.
///
/// <para>Enforces Trax.Docs/adr/0016-a-junction-chain-is-a-declaration-not-a-step-of-the-work.md.</para>
/// </summary>
[Property("adr", "Trax.Docs/adr/0016-a-junction-chain-is-a-declaration-not-a-step-of-the-work.md")]
public class AddServicesSlotTests : TestSetup
{
    [Test]
    public async Task OneObjectPassedUnderTwoInterfaces_FillsBothSlots()
    {
        var store = new Store();

        var result = await new TwoRolesTrain(store).RunEither("x");

        result.IsRight.Should().BeTrue(result.IsLeft ? result.Swap().ValueUnsafe().Message : "");
        result.ValueUnsafe().Should().Be("read+write");
    }

    [Test]
    public void OneObjectPassedUnderTwoInterfaces_VerifiesClean() =>
        new TwoRolesTrain(new Store()).DeclaredChain().Refusals.Should().BeEmpty();

    [Test]
    public void AnObjectImplementingAnEarlierSlotsInterface_DoesNotOverwriteThatSlot()
    {
        var reader = new Store();
        var readerAndWriter = new Store();

        var monad = new TwoRolesTrain(reader).Activate("x");
        monad.AddServices<IReader, IWriter>(reader, readerAndWriter);

        monad.Exception.Should().BeNull();
        monad
            .Memory[typeof(IReader)]
            .Should()
            .BeSameAs(reader, "the second service was passed as IWriter, not IReader");
        monad.Memory[typeof(IWriter)].Should().BeSameAs(readerAndWriter);
    }

    [Test]
    public void AnObjectPassedUnderAnInterfaceItDoesNotImplement_IsAFailure()
    {
        var monad = new TwoRolesTrain(new Store()).Activate("x");

        monad.AddServices([new ReadOnly()], [typeof(IWriter)]);

        monad.Exception.Should().NotBeNull();
        monad.Memory.Should().NotContainKey(typeof(IWriter));
    }

    [TestCase(false, TestName = "AFailingServiceAfterAGoodOne_StoresNoneOfTheCallsServices(class)")]
    [TestCase(true, TestName = "AFailingServiceAfterAGoodOne_StoresNoneOfTheCallsServices(struct)")]
    public void AFailingServiceAfterAGoodOne_StoresNoneOfTheCallsServices(bool failAsStruct)
    {
        var monad = new TwoRolesTrain(new Store()).Activate("x");
        object failing = failAsStruct ? 5 : new ReadOnly();

        monad.AddServices([new Store(), failing], [typeof(IReader), typeof(IWriter)]);

        monad.Exception.Should().NotBeNull();
        monad
            .Memory.Should()
            .NotContainKey(typeof(IReader), "a failed call leaves Memory as it found it");
    }

    [Test]
    public void AStructPassedAsAService_IsRefusedWhileTheChainIsRead() =>
        new StructServiceTrain()
            .DeclaredChain()
            .Refusals.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("struct");

    public interface IReader
    {
        string Read();
    }

    public interface IWriter
    {
        string Write();
    }

    public interface IValue { }

    private sealed class Store : IReader, IWriter
    {
        public string Read() => "read";

        public string Write() => "write";
    }

    private sealed class ReadOnly : IReader
    {
        public string Read() => "read";
    }

    private readonly struct StructValue : IValue { }

    private sealed class UseBoth(IReader reader, IWriter writer) : Junction<string, string>
    {
        public override Task<string> Run(string input) =>
            Task.FromResult($"{reader.Read()}+{writer.Write()}");
    }

    private sealed class TwoRolesTrain(Store store) : Train<string, string>
    {
        protected override Task<Either<Exception, string>> Junctions() =>
            AddServices<IReader, IWriter>(store, store).Chain<UseBoth>().Resolve();
    }

    private sealed class StructServiceTrain : Train<string, string>
    {
        protected override Task<Either<Exception, string>> Junctions() =>
            AddServices<IValue>(new StructValue()).Chain<Echo>().Resolve();
    }

    private sealed class Echo : Junction<string, string>
    {
        public override Task<string> Run(string input) => Task.FromResult(input);
    }
}
