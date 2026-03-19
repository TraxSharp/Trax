using System.Xml;
using FluentAssertions;
using NUnit.Framework;
using Trax.Samples.TestRunner.Trains.RunTests.Junctions;

namespace Trax.Samples.TestRunner.Tests.UnitTests;

[TestFixture]
public class ExecuteTestsJunctionTests
{
    #region ResolveDllPath

    [Test]
    public void ResolveDllPath_ReturnsExpectedPath()
    {
        var csproj =
            "/home/user/Repos/Trax/Trax.Core/tests/Trax.Core.Tests.Unit/Trax.Core.Tests.Unit.csproj";

        var result = ExecuteTestsJunction.ResolveDllPath(csproj);

        result
            .Should()
            .Be(
                "/home/user/Repos/Trax/Trax.Core/tests/Trax.Core.Tests.Unit/bin/Debug/net10.0/Trax.Core.Tests.Unit.dll"
            );
    }

    #endregion

    #region ParseResults — All Passing

    [Test]
    public void ParseResults_AllPassing_ReturnsCorrectCounts()
    {
        var xml = CreateResultXml(
            testcasecount: 5,
            passed: 5,
            failed: 0,
            skipped: 0,
            duration: 1.234
        );

        var result = ExecuteTestsJunction.ParseResults("TestProject", xml);

        result.ProjectName.Should().Be("TestProject");
        result.Total.Should().Be(5);
        result.Passed.Should().Be(5);
        result.Failed.Should().Be(0);
        result.Skipped.Should().Be(0);
        result.DurationSeconds.Should().BeApproximately(1.234, 0.001);
        result.FailedTests.Should().BeEmpty();
    }

    #endregion

    #region ParseResults — Mixed Pass/Fail/Skip

    [Test]
    public void ParseResults_MixedResults_ReturnsCorrectCounts()
    {
        var xml = CreateResultXml(
            testcasecount: 10,
            passed: 7,
            failed: 2,
            skipped: 1,
            duration: 3.5,
            failedTestCases:
            [
                (
                    "Namespace.Class.TestA",
                    0.1,
                    "Expected true but was false",
                    "at TestA() in File.cs:line 42"
                ),
                ("Namespace.Class.TestB", 0.2, "Value was null", null),
            ]
        );

        var result = ExecuteTestsJunction.ParseResults("MixedProject", xml);

        result.Total.Should().Be(10);
        result.Passed.Should().Be(7);
        result.Failed.Should().Be(2);
        result.Skipped.Should().Be(1);
        result.FailedTests.Should().HaveCount(2);

        result.FailedTests[0].FullName.Should().Be("Namespace.Class.TestA");
        result.FailedTests[0].Outcome.Should().Be("Failed");
        result.FailedTests[0].ErrorMessage.Should().Be("Expected true but was false");
        result.FailedTests[0].StackTrace.Should().Contain("File.cs:line 42");

        result.FailedTests[1].FullName.Should().Be("Namespace.Class.TestB");
        result.FailedTests[1].ErrorMessage.Should().Be("Value was null");
        result.FailedTests[1].StackTrace.Should().BeNull();
    }

    #endregion

    #region ParseResults — Zero Tests

    [Test]
    public void ParseResults_ZeroTests_ReturnsZeroCounts()
    {
        var xml = CreateResultXml(
            testcasecount: 0,
            passed: 0,
            failed: 0,
            skipped: 0,
            duration: 0.01
        );

        var result = ExecuteTestsJunction.ParseResults("EmptyProject", xml);

        result.Total.Should().Be(0);
        result.Passed.Should().Be(0);
        result.Failed.Should().Be(0);
        result.FailedTests.Should().BeEmpty();
    }

    #endregion

    #region ParseResults — Duration Parsing

    [Test]
    public void ParseResults_ParsesDurationCorrectly()
    {
        var xml = CreateResultXml(
            testcasecount: 1,
            passed: 1,
            failed: 0,
            skipped: 0,
            duration: 42.567
        );

        var result = ExecuteTestsJunction.ParseResults("DurationTest", xml);

        result.DurationSeconds.Should().BeApproximately(42.567, 0.001);
    }

    #endregion

    #region Helpers

    private static XmlNode CreateResultXml(
        int testcasecount,
        int passed,
        int failed,
        int skipped,
        double duration,
        List<(string fullname, double dur, string message, string? stack)>? failedTestCases = null
    )
    {
        var doc = new XmlDocument();

        var failedCasesXml = "";
        if (failedTestCases != null)
        {
            foreach (var (fullname, dur, message, stack) in failedTestCases)
            {
                var stackXml =
                    stack != null ? $"<stack-trace><![CDATA[{stack}]]></stack-trace>" : "";

                failedCasesXml += $"""
                    <test-case fullname="{fullname}" result="Failed" duration="{dur.ToString(
                        System.Globalization.CultureInfo.InvariantCulture
                    )}">
                      <failure>
                        <message><![CDATA[{message}]]></message>
                        {stackXml}
                      </failure>
                    </test-case>
                    """;
            }
        }

        var xml = $"""
            <test-run testcasecount="{testcasecount}" passed="{passed}" failed="{failed}" skipped="{skipped}" duration="{duration.ToString(
                System.Globalization.CultureInfo.InvariantCulture
            )}">
              <test-suite name="Assembly">
                {failedCasesXml}
              </test-suite>
            </test-run>
            """;

        doc.LoadXml(xml);
        return doc.DocumentElement!;
    }

    #endregion
}
