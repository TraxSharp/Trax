using Trax.Core.Junction;
using Trax.Samples.TestRunner.Services;

namespace Trax.Samples.TestRunner.Trains.RunTests.Junctions;

/// <summary>
/// Resolves the requested project name against <see cref="TestProjectRegistry"/>. The next
/// junctions build and load whatever path they are given, so this is the only thing standing
/// between a caller and running arbitrary MSBuild or test code on this machine: a name the
/// registry did not discover is refused.
/// </summary>
public class ResolveProjectJunction(TestProjectRegistry registry) : Junction<RunTestsInput, TestRun>
{
    public override Task<TestRun> Run(RunTestsInput input)
    {
        var project =
            registry.Find(input.ProjectName)
            ?? throw new InvalidOperationException(
                $"'{input.ProjectName}' is not a registered test project."
            );

        return Task.FromResult(new TestRun { Project = project, Build = input.Build });
    }
}
