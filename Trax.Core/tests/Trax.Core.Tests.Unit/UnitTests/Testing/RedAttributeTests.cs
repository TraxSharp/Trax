using AwesomeAssertions;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;
using NUnit.Framework.Internal.Commands;

namespace Trax.Core.Tests.Unit.UnitTests.Testing;

/// <summary>
/// The <see cref="RedAttribute"/> every folder's tests compile: a red test runs and its verdict is inverted. The
/// first three tests fail inside and are reported as passed, which is the attribute working end to end; the rest
/// run its command directly, which is how the passing-test half is proved without a test that fails the build.
/// </summary>
[TestFixture]
public class RedAttributeTests
{
    [Test]
    [Red("self-test")]
    public void A_red_test_that_throws_is_reported_as_passed() =>
        throw new InvalidOperationException("The code this test waits for is not written yet.");

    [Test]
    [Red("self-test")]
    public async Task An_async_red_test_whose_assertion_fails_is_reported_as_passed()
    {
        await Task.Yield();
        1.Should().Be(2, "a red test's assertion fails until its code arrives");
    }

    [TestCase(1)]
    [TestCase(2)]
    [Red("self-test")]
    public void Every_case_of_a_red_test_case_method_is_inverted(int value) =>
        value.Should().BeNegative("each case fails, so each is reported as passed");

    [Test]
    public void A_failing_test_becomes_a_pass_carrying_the_first_line_of_its_failure()
    {
        var result = Run(c => c.CurrentResult.SetResult(ResultState.Failure, "first\nsecond"));

        result.ResultState.Should().Be(ResultState.Success);
        result.Message.Should().Be("Red as expected (waiting): first");
    }

    [Test]
    public void A_throwing_test_becomes_a_pass()
    {
        var result = Run(_ => throw new InvalidOperationException("boom"));

        result.ResultState.Should().Be(ResultState.Success);
        result.Message.Should().StartWith("Red as expected (waiting): ").And.Contain("boom");
    }

    [Test]
    public void A_passing_test_becomes_a_failure_asking_for_the_attribute_to_come_off()
    {
        var result = Run(c => c.CurrentResult.SetResult(ResultState.Success));

        result.ResultState.Should().Be(ResultState.Failure);
        result.Message.Should().Be("Passed while marked [Red] (waiting): remove the attribute");
    }

    [Test]
    public void An_ignored_test_is_left_as_it_is()
    {
        var result = Run(c => c.CurrentResult.SetResult(ResultState.Ignored, "later"));

        result.ResultState.Should().Be(ResultState.Ignored);
        result.Message.Should().Be("later");
    }

    [Test]
    public void An_inconclusive_test_is_left_as_it_is()
    {
        var result = Run(c => c.CurrentResult.SetResult(ResultState.Inconclusive, "unknown"));

        result.ResultState.Should().Be(ResultState.Inconclusive);
    }

    [Test]
    public void The_attribute_adds_the_red_category_and_says_why()
    {
        var test = Probe();

        new RedAttribute("waiting").ApplyToTest(test);

        test.Properties[PropertyNames.Category]
            .Cast<object>()
            .Should()
            .Contain(RedAttribute.Category);
        test.Properties[RedAttribute.PropertyName]
            .Cast<object>()
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be("waiting");
    }

    private static TestResult Run(Action<TestExecutionContext> body)
    {
        var test = Probe();
        var context = new TestExecutionContext { CurrentResult = test.MakeTestResult() };
        var command = new RedAttribute("waiting").Wrap(new BodyCommand(test, body));
        return command.Execute(context);
    }

    private static TestMethod Probe() =>
        new(new MethodWrapper(typeof(RedAttributeTests), nameof(ProbeTarget)));

    // Never run as a test: the method the probe test names.
    private static void ProbeTarget() { }

    private sealed class BodyCommand(Test test, Action<TestExecutionContext> body)
        : TestCommand(test)
    {
        public override TestResult Execute(TestExecutionContext context)
        {
            body(context);
            return context.CurrentResult;
        }
    }
}
