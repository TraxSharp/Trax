namespace Trax.Adr.Guard.Tests.Tests;

/// <summary>
/// The opt-out reason is the one place a human can talk the build out of enforcing
/// something, so what it accepts matters as much as what the guards check.
///
/// <para>
/// Enforces <c>adr/0004-tests-assert-with-fluentassertions.md</c>: these assertions carry
/// their reason in the <c>because</c> argument, which is what a failing run prints, rather
/// than leaving it in the test name where a failure never shows it.
/// </para>
/// </summary>
[TestFixture]
public class ReasonsTests
{
    [Test]
    public void Problem_RealReason_IsAccepted()
    {
        Reasons.Problem(Sample.GoodReason).Should().BeNull();
    }

    [Test]
    public void Problem_TooShort_IsRejected()
    {
        Reasons.Problem("it is hard").Should().Contain("needs a real reason");
    }

    [Test]
    public void Problem_ExactlyAtTheMinimum_IsAccepted()
    {
        var reason = new string('a', Reasons.MinimumLength);

        Reasons.Problem(reason).Should().BeNull();
    }

    [Test]
    public void Problem_OneCharacterShortOfTheMinimum_IsRejected()
    {
        var reason = new string('a', Reasons.MinimumLength - 1);

        Reasons.Problem(reason).Should().Contain("needs a real reason");
    }

    [TestCase("we have not written the guard yet, and it is a large piece of work to do properly")]
    [TestCase("not yet, because the surrounding refactor has to land before this is checkable")]
    [TestCase("a TODO for whoever picks this up next, once the census has actually been built")]
    [TestCase("this will be written when somebody has the time to do the census properly here")]
    [TestCase("deferred to a follow-up because the change is larger than this one pull request")]
    [TestCase("eventually the census will cover it, but that is a much larger piece of work")]
    public void Problem_Deferral_IsRejected_EvenWhenLongEnough(string reason)
    {
        reason
            .Length.Should()
            .BeGreaterThanOrEqualTo(
                Reasons.MinimumLength,
                "the length must not be what rejects it, per "
                    + "adr/0004-tests-assert-with-fluentassertions.md"
            );

        Reasons.Problem(reason).Should().Contain("reads as a deferral");
    }

    [Test]
    public void Problem_LegitimateReasonMentioningEnforcement_IsAccepted()
    {
        const string reason =
            "a framework choice is not a rule code can break: nothing here compiles without it, "
            + "so there is no state of the repo where this is violated.";

        Reasons.Problem(reason).Should().BeNull();
    }
}
