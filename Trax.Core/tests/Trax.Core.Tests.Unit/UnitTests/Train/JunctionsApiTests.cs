using FluentAssertions;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using Trax.Core.Junction;
using Trax.Core.Train;

namespace Trax.Core.Tests.Unit.UnitTests.Train;

/// <summary>
/// Exercises the protected Chain/IChain/ShortCircuit/Extract/AddServices wrappers on Train
/// that are reachable only by overriding Junctions() and using the bare protected methods
/// (which forward to the internal Monad set up by the default RunInternal).
/// </summary>
public class JunctionsApiTests : TestSetup
{
    [Test]
    public async Task Junctions_DefaultBaseImpl_ThrowsNotImplemented()
    {
        var train = new EmptyJunctionsTrain();

        var either = await train.RunEither("x");

        either.IsLeft.Should().BeTrue();
        either.Swap().ValueUnsafe().Should().BeOfType<NotImplementedException>();
    }

    [Test]
    public async Task RunInternal_JunctionsThrows_CapturesIntoLeft()
    {
        var train = new ThrowingJunctionsTrain();

        var either = await train.RunEither("x");

        either.IsLeft.Should().BeTrue();
        either.Swap().ValueUnsafe().Message.Should().Be("boom");
    }

    [Test]
    public async Task Junctions_ProtectedChainTJunctionInstance_RunsViaTrainWrapper()
    {
        var train = new ProtectedChainInstanceTrain();

        var result = await train.Run("hi");

        result.Should().Be("hi-done");
    }

    [Test]
    public async Task Junctions_ProtectedChainTJunctionTypeOnly_RunsViaTrainWrapper()
    {
        var train = new ProtectedChainTypeOnlyTrain();

        var result = await train.Run("hi");

        result.Should().Be("hi-done");
    }

    [Test]
    public async Task Junctions_ProtectedIChain_RunsRegisteredInterfaceJunction()
    {
        var train = new ProtectedIChainTrain();

        var result = await train.Run("hi");

        result.Should().Be("hi-done");
    }

    [Test]
    public async Task Junctions_ProtectedExtract_PullsFieldFromMemory()
    {
        var train = new ProtectedExtractFromMemoryTrain();

        var result = await train.Run("hi");

        result.Should().Be("payload-extracted");
    }

    [Test]
    public async Task Junctions_ProtectedExtractWithInput_PullsFieldFromInput()
    {
        var train = new ProtectedExtractFromInputTrain();

        var result = await train.Run("hi");

        result.Should().Be("payload-extracted");
    }

    [Test]
    public async Task Junctions_ProtectedShortCircuitTypeOnly_EndsChainEarly()
    {
        var train = new ProtectedShortCircuitTypeOnlyTrain();

        var result = await train.Run("hi");

        result.Should().Be("short-circuited");
    }

    [Test]
    public async Task Junctions_ProtectedShortCircuitInstance_EndsChainEarly()
    {
        var train = new ProtectedShortCircuitInstanceTrain();

        var result = await train.Run("hi");

        result.Should().Be("short-circuited");
    }

    [Test]
    public async Task Junctions_ProtectedAddServicesT1ThroughT7_AreReachableFromTrain()
    {
        var train = new ProtectedAddServicesTrain();

        var result = await train.Run("hi");

        result.Should().Be("seven");
    }

    #region Test fixtures

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

    private class DoneJunction : Junction<string, string>
    {
        public override Task<string> Run(string input) => Task.FromResult($"{input}-done");
    }

    private interface IDoneJunction : IJunction<string, string> { }

    private class IDoneJunctionImpl : Junction<string, string>, IDoneJunction
    {
        public override Task<string> Run(string input) => Task.FromResult($"{input}-done");
    }

    private class HasInner
    {
        public string Inner { get; set; } = "payload";
    }

    private class ProduceHasInnerJunction : Junction<string, HasInner>
    {
        public override Task<HasInner> Run(string input) => Task.FromResult(new HasInner());
    }

    private class ExtractedJunction : Junction<string, string>
    {
        public override Task<string> Run(string input) => Task.FromResult($"{input}-extracted");
    }

    private class ShortCircuitJunctionShort : Junction<string, string>
    {
        public override Task<string> Run(string input) => Task.FromResult("short-circuited");
    }

    private class ConsumeAllServicesJunction : Junction<string, string>
    {
        public ConsumeAllServicesJunction(IS1 s1, IS2 s2, IS3 s3, IS4 s4, IS5 s5, IS6 s6, IS7 s7)
        { }

        public override Task<string> Run(string input) => Task.FromResult("seven");
    }

    private class EmptyJunctionsTrain : Train<string, string> { }

    private class ThrowingJunctionsTrain : Train<string, string>
    {
        protected override Task<Either<Exception, string>> Junctions() =>
            throw new InvalidOperationException("boom");
    }

    private class ProtectedChainInstanceTrain : Train<string, string>
    {
        protected override async Task<Either<Exception, string>> Junctions() =>
            await Chain(new DoneJunction()).Resolve();
    }

    private class ProtectedChainTypeOnlyTrain : Train<string, string>
    {
        protected override async Task<Either<Exception, string>> Junctions() =>
            await Chain<DoneJunction>().Resolve();
    }

    private class ProtectedIChainTrain : Train<string, string>
    {
        protected override async Task<Either<Exception, string>> Junctions()
        {
            AddServices<IDoneJunction>(new IDoneJunctionImpl());
            return await IChain<IDoneJunction>().Resolve();
        }
    }

    private class ProtectedExtractFromMemoryTrain : Train<string, string>
    {
        protected override async Task<Either<Exception, string>> Junctions()
        {
            await Chain(new ProduceHasInnerJunction());
            Extract<HasInner, string>();
            return await Chain(new ExtractedJunction()).Resolve();
        }
    }

    private class ProtectedExtractFromInputTrain : Train<string, string>
    {
        protected override async Task<Either<Exception, string>> Junctions()
        {
            Extract<HasInner, string>(new HasInner());
            return await Chain(new ExtractedJunction()).Resolve();
        }
    }

    private class ProtectedShortCircuitTypeOnlyTrain : Train<string, string>
    {
        protected override async Task<Either<Exception, string>> Junctions() =>
            await ShortCircuit<ShortCircuitJunctionShort>().Chain(new DoneJunction()).Resolve();
    }

    private class ProtectedShortCircuitInstanceTrain : Train<string, string>
    {
        protected override async Task<Either<Exception, string>> Junctions() =>
            await ShortCircuit(new ShortCircuitJunctionShort()).Chain(new DoneJunction()).Resolve();
    }

    private class ProtectedAddServicesTrain : Train<string, string>
    {
        protected override async Task<Either<Exception, string>> Junctions()
        {
            AddServices<IS1>(new S1());
            AddServices<IS1, IS2>(new S1(), new S2());
            AddServices<IS1, IS2, IS3>(new S1(), new S2(), new S3());
            AddServices<IS1, IS2, IS3, IS4>(new S1(), new S2(), new S3(), new S4());
            AddServices<IS1, IS2, IS3, IS4, IS5>(new S1(), new S2(), new S3(), new S4(), new S5());
            AddServices<IS1, IS2, IS3, IS4, IS5, IS6>(
                new S1(),
                new S2(),
                new S3(),
                new S4(),
                new S5(),
                new S6()
            );
            AddServices<IS1, IS2, IS3, IS4, IS5, IS6, IS7>(
                new S1(),
                new S2(),
                new S3(),
                new S4(),
                new S5(),
                new S6(),
                new S7()
            );

            return await Chain(
                    new ConsumeAllServicesJunction(
                        new S1(),
                        new S2(),
                        new S3(),
                        new S4(),
                        new S5(),
                        new S6(),
                        new S7()
                    )
                )
                .Resolve();
        }
    }

    #endregion
}
