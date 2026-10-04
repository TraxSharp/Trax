using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Core.Monad;
using Trax.Core.Train;

namespace Trax.Core.Tests.Unit.UnitTests.Train;

/// <summary>
/// <c>IChain</c> resolves a junction by an interface, and <c>IJunction&lt;TIn, TOut&gt;</c> is the
/// interface every junction already has.
/// </summary>
public class JunctionInterfaceItselfTests : TestSetup
{
    [Test]
    public async Task IChain_OfIJunctionItself_RunsTheRegisteredJunction()
    {
        var services = new ServiceCollection()
            .AddTransient<IJunction<string, int>, Length>()
            .BuildServiceProvider();

        var result = await new LengthTrain(services).RunEither("abcd");

        result.IsRight.Should().BeTrue(result.IsLeft ? result.Swap().ValueUnsafe().Message : "");
        result.ValueUnsafe().Should().Be(4);
    }

    [Test]
    public void DeclaredChain_OfIJunctionItself_RecordsItsTypesWithoutARefusal()
    {
        var chain = new LengthTrain(new ServiceCollection().BuildServiceProvider()).DeclaredChain();

        chain.Refusals.Should().BeEmpty();
        chain
            .Steps.Should()
            .ContainEquivalentOf(
                new ChainStep(
                    ChainStepKind.IChain,
                    typeof(IJunction<string, int>),
                    typeof(string),
                    typeof(int)
                )
            );
    }

    [Test]
    public async Task Chain_OfATypeThatIsNotAJunction_NamesThatTypeInTheFailure()
    {
        var result = await new NotAJunctionTrain().RunEither("x");

        result.IsLeft.Should().BeTrue();
        result
            .Swap()
            .ValueUnsafe()
            .Message.Should()
            .Contain(nameof(NotAJunction), "the failure has to say which type is wrong");
    }

    public sealed class Length : Junction<string, int>
    {
        public override Task<int> Run(string input) => Task.FromResult(input.Length);
    }

    public sealed class NotAJunction;

    private sealed class LengthTrain(IServiceProvider services) : Train<string, int>
    {
        protected override Monad<string, int> NewMonad() => new(this, services, CancellationToken);

        protected override Task<Either<Exception, int>> Junctions() =>
            IChain<IJunction<string, int>>().Resolve();
    }

    private sealed class NotAJunctionTrain : Train<string, string>
    {
        protected override Task<Either<Exception, string>> Junctions() =>
            Chain(new NotAJunction()).Resolve();
    }
}
