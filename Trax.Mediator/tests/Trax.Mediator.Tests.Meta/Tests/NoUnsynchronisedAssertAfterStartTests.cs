using Trax.Core.Testing;
using Trax.Core.Testing.Guards;

namespace Trax.Mediator.Tests.Meta.Tests;

/// <summary>
/// A substitute is checked only once the work a start handed to the background has happened.
/// The check is the shipped <see cref="HygieneGuards.NoUnsynchronisedAssertAfterStart"/>: after a
/// <c>StartAsync</c>, a <c>Received</c> or <c>DidNotReceive</c> in the same method must follow a
/// wait for that work, unless the start carries a <c>synchronous-start:</c> comment saying it
/// finishes its work before returning.
///
/// <para>Enforces <c>Trax.Docs/adr/0006-tests-synchronise-on-a-signal.md</c>.</para>
/// </summary>
[Property("adr", "Trax.Docs/adr/0006-tests-synchronise-on-a-signal.md")]
[TestFixture]
public class NoUnsynchronisedAssertAfterStartTests
{
    [Test]
    public void TestSources_DoNotCheckASubstituteBeforeTheStartedWorkRan()
    {
        var result = HygieneGuards.NoUnsynchronisedAssertAfterStart(
            new ArchitectureGuardOptions { RepoRootOverride = RepoRoot.Path }
        );

        result
            .Inspected.Should()
            .BeGreaterThan(
                0,
                "the guard inspected no test sources under tests/, so it checked nothing"
            );
        result
            .Offenders.Should()
            .BeEmpty(
                "Trax.Docs/adr/0006-tests-synchronise-on-a-signal.md: wait for the started work "
                    + "before checking what it did. "
                    + result.FailureMessage
            );
    }
}
