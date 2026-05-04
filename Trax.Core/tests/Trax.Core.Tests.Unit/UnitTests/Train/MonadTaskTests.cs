using FluentAssertions;
using LanguageExt;
using Trax.Core.Junction;
using Trax.Core.Monad;
using Trax.Core.Train;

namespace Trax.Core.Tests.Unit.UnitTests.Train;

public class MonadTaskTests : TestSetup
{
    [Test]
    public void MonadTask_ImplicitConversion_YieldsSourceTask()
    {
        var monad = new TestTrain().Activate("hi");
        var mt = Task.FromResult(monad).AsMonadTask();

        Task<Monad<string, string>> task = mt;

        task.Should().BeSameAs(mt.AsTask());
    }

    [Test]
    public async Task MonadTask_AsTask_ReturnsAwaitableSource()
    {
        var monad = new TestTrain().Activate("hi");
        var mt = Task.FromResult(monad).AsMonadTask();

        var resolved = await mt.AsTask();

        resolved.Should().BeSameAs(monad);
    }

    [Test]
    public async Task MonadTask_ConfigureAwait_ReturnsAwaitable()
    {
        var monad = new TestTrain().Activate("hi");
        var mt = Task.FromResult(monad).AsMonadTask();

        var resolved = await mt.ConfigureAwait(false);

        resolved.Should().BeSameAs(monad);
    }

    [Test]
    public async Task MonadTask_ChainExplicit_RunsJunction()
    {
        var monad = new TestTrain().Activate("hi");

        var result = await Task.FromResult(monad)
            .AsMonadTask()
            .Chain<DoneJunction, string, string>(new DoneJunction())
            .Resolve();

        result.IsRight.Should().BeTrue();
    }

    [Test]
    public async Task MonadTask_ChainExplicitWithDefaultCtor_RunsJunction()
    {
        var monad = new TestTrain().Activate("hi");

        var result = await Task.FromResult(monad)
            .AsMonadTask()
            .Chain<DoneJunction, string, string>()
            .Resolve();

        result.IsRight.Should().BeTrue();
    }

    [Test]
    public async Task MonadTask_ChainUnit_RunsJunction()
    {
        var monad = new TestTrain().Activate("hi");

        var result = await Task.FromResult(monad)
            .AsMonadTask()
            .Chain<UnitJunction, string>(new UnitJunction())
            .Resolve(Either<Exception, string>.Right("done"));

        result.IsRight.Should().BeTrue();
    }

    [Test]
    public async Task MonadTask_ChainUnitWithDefaultCtor_RunsJunction()
    {
        var monad = new TestTrain().Activate("hi");

        var result = await Task.FromResult(monad)
            .AsMonadTask()
            .Chain<UnitJunction, string>()
            .Resolve(Either<Exception, string>.Right("done"));

        result.IsRight.Should().BeTrue();
    }

    [Test]
    public async Task MonadTask_ShortCircuitTypeOnly_EndsChainEarly()
    {
        var monad = new TestTrain().Activate("hi");

        var result = await Task.FromResult(monad)
            .AsMonadTask()
            .ShortCircuit<ShortCircuitJunction>()
            .Resolve();

        result.IsRight.Should().BeTrue();
    }

    [Test]
    public async Task MonadTask_ShortCircuitInstance_EndsChainEarly()
    {
        var monad = new TestTrain().Activate("hi");

        var result = await Task.FromResult(monad)
            .AsMonadTask()
            .ShortCircuit(new ShortCircuitJunction())
            .Resolve();

        result.IsRight.Should().BeTrue();
    }

    [Test]
    public async Task MonadTask_ExtractFromMemory_PullsField()
    {
        var monad = new TestTrain().Activate("hi", new HasInner());

        var result = await Task.FromResult(monad)
            .AsMonadTask()
            .Extract<HasInner, string>()
            .Resolve(Either<Exception, string>.Right("ok"));

        result.IsRight.Should().BeTrue();
    }

    [Test]
    public async Task MonadTask_ExtractFromInput_PullsField()
    {
        var monad = new TestTrain().Activate("hi");

        var result = await Task.FromResult(monad)
            .AsMonadTask()
            .Extract<HasInner, string>(new HasInner())
            .Resolve(Either<Exception, string>.Right("ok"));

        result.IsRight.Should().BeTrue();
    }

    [Test]
    public async Task MonadTask_AddServices1Through7_AllReachable()
    {
        var monad = new TestTrain().Activate("hi");

        var result = await Task.FromResult(monad)
            .AsMonadTask()
            .AddServices<IS1>(new S1())
            .AddServices<IS2, IS3>(new S2(), new S3())
            .AddServices<IS4, IS5, IS6>(new S4(), new S5(), new S6())
            .AddServices<IS7>(new S7())
            .AddServices<IS1, IS2, IS3, IS4>(new S1(), new S2(), new S3(), new S4())
            .AddServices<IS1, IS2, IS3, IS4, IS5>(new S1(), new S2(), new S3(), new S4(), new S5())
            .AddServices<IS1, IS2, IS3, IS4, IS5, IS6>(
                new S1(),
                new S2(),
                new S3(),
                new S4(),
                new S5(),
                new S6()
            )
            .AddServices<IS1, IS2, IS3, IS4, IS5, IS6, IS7>(
                new S1(),
                new S2(),
                new S3(),
                new S4(),
                new S5(),
                new S6(),
                new S7()
            )
            .Resolve(Either<Exception, string>.Right("ok"));

        result.IsRight.Should().BeTrue();
    }

    #region Test fixtures

    private class HasInner
    {
        public string Inner { get; set; } = "payload";
    }

    private class DoneJunction : Junction<string, string>
    {
        public override Task<string> Run(string input) => Task.FromResult($"{input}-done");

        public DoneJunction() { }
    }

    private class UnitJunction : Junction<string, LanguageExt.Unit>
    {
        public override Task<LanguageExt.Unit> Run(string input) =>
            Task.FromResult(LanguageExt.Unit.Default);

        public UnitJunction() { }
    }

    private class ShortCircuitJunction : Junction<string, string>
    {
        public override Task<string> Run(string input) => Task.FromResult("shorted");

        public ShortCircuitJunction() { }
    }

    private interface IS1 { }

    private interface IS2 { }

    private interface IS3 { }

    private interface IS4 { }

    private interface IS5 { }

    private interface IS6 { }

    private interface IS7 { }

    private class S1 : IS1 { }

    private class S2 : IS2 { }

    private class S3 : IS3 { }

    private class S4 : IS4 { }

    private class S5 : IS5 { }

    private class S6 : IS6 { }

    private class S7 : IS7 { }

    private class TestTrain : Train<string, string>
    {
        protected override Task<Either<Exception, string>> RunInternal(string input) =>
            throw new NotImplementedException();
    }

    #endregion
}
