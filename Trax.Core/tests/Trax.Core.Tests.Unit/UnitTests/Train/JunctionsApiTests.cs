using AwesomeAssertions;
using Trax.Core.Functional;
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

        var result = await train.Run("hi", CancellationToken.None);

        result.Should().Be("hi-done");
    }

    [Test]
    public async Task Junctions_ProtectedChainTJunctionTypeOnly_RunsViaTrainWrapper()
    {
        var train = new ProtectedChainTypeOnlyTrain();

        var result = await train.Run("hi", CancellationToken.None);

        result.Should().Be("hi-done");
    }

    [Test]
    public async Task Junctions_ProtectedIChain_RunsRegisteredInterfaceJunction()
    {
        var train = new ProtectedIChainTrain();

        var result = await train.Run("hi", CancellationToken.None);

        result.Should().Be("hi-done");
    }

    [Test]
    public async Task Junctions_ProtectedExtract_PullsFieldFromMemory()
    {
        var train = new ProtectedExtractFromMemoryTrain();

        var result = await train.Run("hi", CancellationToken.None);

        result.Should().Be("payload-extracted");
    }

    [Test]
    public async Task Junctions_ProtectedExtractWithInput_PullsFieldFromInput()
    {
        var train = new ProtectedExtractFromInputTrain();

        var result = await train.Run("hi", CancellationToken.None);

        result.Should().Be("payload-extracted");
    }

    [Test]
    public async Task Junctions_ProtectedShortCircuitTypeOnly_EndsChainEarly()
    {
        var train = new ProtectedShortCircuitTypeOnlyTrain();

        var result = await train.Run("hi", CancellationToken.None);

        result.Should().Be("short-circuited");
    }

    [Test]
    public async Task Junctions_ProtectedShortCircuitInstance_EndsChainEarly()
    {
        var train = new ProtectedShortCircuitInstanceTrain();

        var result = await train.Run("hi", CancellationToken.None);

        result.Should().Be("short-circuited");
    }

    [Test]
    public async Task Junctions_ProtectedAddServicesT1ThroughT7_AreReachableFromTrain()
    {
        var train = new ProtectedAddServicesTrain();

        var result = await train.Run("hi", CancellationToken.None);

        result.Should().Be("seven");
    }

    #region Test fixtures

    private interface IS1;

    private interface IS2;

    private interface IS3;

    private interface IS4;

    private interface IS5;

    private interface IS6;

    private interface IS7;

    private sealed class S1 : IS1;

    private sealed class S2 : IS2;

    private sealed class S3 : IS3;

    private sealed class S4 : IS4;

    private sealed class S5 : IS5;

    private sealed class S6 : IS6;

    private sealed class S7 : IS7;

    private sealed class DoneJunction : Junction<string, string>
    {
        public override Task<string> Run(string input) => Task.FromResult($"{input}-done");
    }

    private interface IDoneJunction : IJunction<string, string>;

    private sealed class IDoneJunctionImpl : Junction<string, string>, IDoneJunction
    {
        public override Task<string> Run(string input) => Task.FromResult($"{input}-done");
    }

    private sealed class HasInner
    {
        public string Inner { get; set; } = "payload";
    }

    private sealed class ProduceHasInnerJunction : Junction<string, HasInner>
    {
        public override Task<HasInner> Run(string input) => Task.FromResult(new HasInner());
    }

    private sealed class ExtractedJunction : Junction<string, string>
    {
        public override Task<string> Run(string input) => Task.FromResult($"{input}-extracted");
    }

    private sealed class ShortCircuitJunctionShort : Junction<string, string>
    {
        public override Task<string> Run(string input) => Task.FromResult("short-circuited");
    }

    private sealed class ConsumeAllServicesJunction : Junction<string, string>
    {
        public ConsumeAllServicesJunction(IS1 s1, IS2 s2, IS3 s3, IS4 s4, IS5 s5, IS6 s6, IS7 s7)
        { }

        public override Task<string> Run(string input) => Task.FromResult("seven");
    }

    private sealed class EmptyJunctionsTrain : Train<string, string>;

    private sealed class ThrowingJunctionsTrain : Train<string, string>
    {
        protected override Task<Either<Exception, string>> Junctions() =>
            throw new InvalidOperationException("boom");
    }

    private sealed class ProtectedChainInstanceTrain : Train<string, string>
    {
        protected override async Task<Either<Exception, string>> Junctions() =>
            await Chain(new DoneJunction()).Resolve().ConfigureAwait(false);
    }

    private sealed class ProtectedChainTypeOnlyTrain : Train<string, string>
    {
        protected override async Task<Either<Exception, string>> Junctions() =>
            await Chain<DoneJunction>().Resolve().ConfigureAwait(false);
    }

    private sealed class ProtectedIChainTrain : Train<string, string>
    {
        protected override async Task<Either<Exception, string>> Junctions()
        {
            AddServices<IDoneJunction>(new IDoneJunctionImpl());
            return await IChain<IDoneJunction>().Resolve().ConfigureAwait(false);
        }
    }

    private sealed class ProtectedExtractFromMemoryTrain : Train<string, string>
    {
        protected override async Task<Either<Exception, string>> Junctions()
        {
            await Chain(new ProduceHasInnerJunction()).ConfigureAwait(false);
            Extract<HasInner, string>();
            return await Chain(new ExtractedJunction()).Resolve().ConfigureAwait(false);
        }
    }

    private sealed class ProtectedExtractFromInputTrain : Train<string, string>
    {
        protected override async Task<Either<Exception, string>> Junctions()
        {
            Extract<HasInner, string>(new HasInner());
            return await Chain(new ExtractedJunction()).Resolve().ConfigureAwait(false);
        }
    }

    private sealed class ProtectedShortCircuitTypeOnlyTrain : Train<string, string>
    {
        protected override async Task<Either<Exception, string>> Junctions() =>
            await ShortCircuit<ShortCircuitJunctionShort>()
                .Chain(new DoneJunction())
                .Resolve()
                .ConfigureAwait(false);
    }

    private sealed class ProtectedShortCircuitInstanceTrain : Train<string, string>
    {
        protected override async Task<Either<Exception, string>> Junctions() =>
            await ShortCircuit(new ShortCircuitJunctionShort())
                .Chain(new DoneJunction())
                .Resolve()
                .ConfigureAwait(false);
    }

    private sealed class ProtectedAddServicesTrain : Train<string, string>
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
                .Resolve()
                .ConfigureAwait(false);
        }
    }

    #endregion
}
