using AwesomeAssertions;
using Trax.StateMachine.Tests.Helpers;

namespace Trax.StateMachine.Tests.Conformance;

/// <summary>
/// The C# engine through the conformance runner, the same runner and the same cases every other engine is checked
/// with (<c>Trax.StateMachine/conformance/PROTOCOL.md</c>). The runner is started with <c>--stdio</c> and this
/// process answers its requests, so no separate C# executable is needed.
/// </summary>
[Category(Categories.CrossEngine)]
public class ConformanceRunnerTests
{
    [Test]
    public void The_CSharp_engine_passes_every_case_of_the_kit()
    {
        var testee = new ConformanceTestee();

        var (exitCode, report) = TwinEngine.RunConformance(testee.Answer);

        exitCode.Should().Be(0, report);
        report.Should().MatchRegex(@"([1-9]\d*) of \1 cases passed\.");
    }

    [Test]
    public void The_CSharp_engine_built_from_each_IR_passes_every_case_of_the_kit()
    {
        var testee = new ConformanceTestee(fromIr: true);

        var (exitCode, report) = TwinEngine.RunConformance(testee.Answer);

        exitCode.Should().Be(0, report);
        report.Should().MatchRegex(@"([1-9]\d*) of \1 cases passed\.");
    }
}
