using AwesomeAssertions;
using Trax.Effect.StateMachine.Tests.Helpers;

namespace Trax.Effect.StateMachine.Tests.Conformance;

/// <summary>
/// The C# engine through the conformance runner, the same runner and the same cases every other engine is checked
/// with (<c>Trax.Api.StateMachine/conformance/PROTOCOL.md</c>). The runner is started with <c>--stdio</c> and this
/// process answers its requests, so no separate C# executable is needed.
/// </summary>
public class ConformanceRunnerTests
{
    [Test]
    public void The_CSharp_engine_passes_every_case_of_the_kit()
    {
        var testee = new ConformanceTestee();

        var (exitCode, report) = TwinEngine.RunConformance(testee.Answer);

        exitCode.Should().Be(0, report);
        report.Should().MatchRegex(@"(\d+) of \1 cases passed\.");
    }
}
