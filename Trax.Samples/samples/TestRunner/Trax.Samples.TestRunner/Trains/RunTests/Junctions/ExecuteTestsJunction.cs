using System.Globalization;
using System.Xml;
using Microsoft.Extensions.Logging;
using NUnit.Engine;
using Trax.Core.Junction;
using Trax.Samples.TestRunner.Models;

namespace Trax.Samples.TestRunner.Trains.RunTests.Junctions;

public class ExecuteTestsJunction(ILogger<ExecuteTestsJunction> logger)
    : Junction<RunTestsInput, RunTestsOutput>
{
    public override Task<RunTestsOutput> Run(RunTestsInput input)
    {
        var dllPath = ResolveDllPath(input.ProjectPath);

        if (!File.Exists(dllPath))
        {
            throw new FileNotFoundException(
                $"Test assembly not found at {dllPath}. Ensure the project was built successfully.",
                dllPath
            );
        }

        logger.LogInformation(
            "Running tests in-process for {ProjectName} from {DllPath}",
            input.ProjectName,
            dllPath
        );

        using var engine = TestEngineActivator.CreateInstance();
        var package = new TestPackage(dllPath);
        using var runner = engine.GetRunner(package);

        try
        {
            var resultXml = runner.Run(listener: null, TestFilter.Empty);
            var result = ParseResults(input.ProjectName, resultXml);

            logger.LogInformation(
                "Tests completed for {ProjectName}: {Passed}/{Total} passed, {Failed} failed",
                input.ProjectName,
                result.Passed,
                result.Total,
                result.Failed
            );

            return Task.FromResult(new RunTestsOutput { Result = result });
        }
        finally
        {
            runner.Unload();
        }
    }

    internal static string ResolveDllPath(string csprojPath)
    {
        var projectDir = Path.GetDirectoryName(csprojPath)!;
        var projectName = Path.GetFileNameWithoutExtension(csprojPath);
        return Path.Combine(projectDir, "bin", "Debug", "net10.0", $"{projectName}.dll");
    }

    internal static TestResult ParseResults(string projectName, XmlNode resultXml)
    {
        var testRun = resultXml;

        var total = GetIntAttribute(testRun, "testcasecount");
        var passed = GetIntAttribute(testRun, "passed");
        var failed = GetIntAttribute(testRun, "failed");
        var skipped = GetIntAttribute(testRun, "skipped");
        var duration = GetDoubleAttribute(testRun, "duration");

        var failedTests = new List<TestCaseResult>();
        var failedCases = testRun.SelectNodes("//test-case[@result='Failed']");

        if (failedCases != null)
        {
            foreach (XmlNode testCase in failedCases)
            {
                var failureNode = testCase.SelectSingleNode("failure");
                failedTests.Add(
                    new TestCaseResult
                    {
                        FullName = testCase.Attributes?["fullname"]?.Value ?? "Unknown",
                        Outcome = "Failed",
                        DurationSeconds = GetDoubleAttribute(testCase, "duration"),
                        ErrorMessage = failureNode?.SelectSingleNode("message")?.InnerText,
                        StackTrace = failureNode?.SelectSingleNode("stack-trace")?.InnerText,
                    }
                );
            }
        }

        return new TestResult
        {
            ProjectName = projectName,
            Total = total,
            Passed = passed,
            Failed = failed,
            Skipped = skipped,
            DurationSeconds = duration,
            FailedTests = failedTests,
        };
    }

    private static int GetIntAttribute(XmlNode node, string name) =>
        int.TryParse(node.Attributes?[name]?.Value, out var value) ? value : 0;

    private static double GetDoubleAttribute(XmlNode node, string name) =>
        double.TryParse(
            node.Attributes?[name]?.Value,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var value
        )
            ? value
            : 0;
}
