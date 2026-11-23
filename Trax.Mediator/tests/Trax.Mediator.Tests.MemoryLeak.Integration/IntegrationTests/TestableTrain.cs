using AwesomeAssertions;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Core.Train;
using Trax.Mediator.Tests.MemoryLeak.Integration.Utils;
using Monad = Trax.Core.Monad;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.IntegrationTests;

/// <summary>
/// Reaches the monad a run left behind, so the Memory tests can assert on what a chain
/// accumulated and on clearing it.
/// </summary>
/// <remarks>
/// The monad is framework state rather than anything a train declares, so it is read by
/// reflection off the base class rather than by having the train hand it out. That keeps this
/// train an ordinary chain declaration: the subject here is Monad.Memory, not the train.
/// </remarks>
public class TestableTrain : Train<SimpleInput, SimpleOutput>
{
    protected override Task<Either<Exception, SimpleOutput>> Junctions() =>
        Chain<ProcessJunction>().Resolve();

    public int GetMemoryCount() => Memory()?.Count ?? 0;

    public void ClearMemory() => Memory()?.Clear();

    private Dictionary<Type, object>? Memory()
    {
        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;

        var monad = typeof(Train<SimpleInput, SimpleOutput>)
            .GetField("_monad", flags)
            ?.GetValue(this);

        return monad?.GetType().GetProperty("Memory", flags)?.GetValue(monad)
            as Dictionary<Type, object>;
    }
}
