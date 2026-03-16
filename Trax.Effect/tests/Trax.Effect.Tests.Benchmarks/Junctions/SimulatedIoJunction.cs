using Trax.Core.Junction;

namespace Trax.Effect.Tests.Benchmarks.Junctions;

public class SimulatedIoJunction : Junction<int, int>
{
    public override async Task<int> Run(int input)
    {
        await Task.Yield();
        return input + 1;
    }
}
