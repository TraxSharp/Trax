using Trax.Core.Junction;

namespace Trax.Effect.Tests.Benchmarks.Junctions;

public class AddOneJunction : Junction<int, int>
{
    public override Task<int> Run(int input) => Task.FromResult(input + 1);
}
