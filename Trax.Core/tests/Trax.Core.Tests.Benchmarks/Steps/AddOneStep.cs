using Trax.Core.Step;

namespace Trax.Core.Tests.Benchmarks.Steps;

public class AddOneStep : Step<int, int>
{
    public override Task<int> Run(int input) => Task.FromResult(input + 1);
}
