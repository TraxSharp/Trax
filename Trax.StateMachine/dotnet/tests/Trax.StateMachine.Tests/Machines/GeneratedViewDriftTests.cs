using System.Diagnostics;
using AwesomeAssertions;
using Trax.StateMachine.Tests.Helpers;

namespace Trax.StateMachine.Tests.Machines;

/// <summary>
/// The committed C# views are what the generator writes today from the committed IRs, as the TypeScript twins under
/// <c>ts/src/machines/</c> are. A change to the generator or to an IR that is not regenerated fails here.
/// </summary>
[Category(Categories.CrossEngine)]
public class GeneratedViewDriftTests
{
    private static readonly TimeSpan NodeTimeout = TimeSpan.FromMinutes(2);
    private const string Namespace = "Trax.StateMachine.Tests.Machines";

    public static IEnumerable<TestCaseData> Machines() =>
        new[] { ("checkout", "Checkout"), ("gauge", "Gauge"), ("ingest", "Ingest"), ("reminder", "Reminder"), ("turnstile", "Turnstile") }
            .Select(m => new TestCaseData(m.Item1, m.Item2).SetName($"The_{m.Item1}_view_is_current"));

    [TestCaseSource(nameof(Machines))]
    public void The_committed_view_is_what_the_generator_writes(string machine, string file)
    {
        var outDir = Directory.CreateTempSubdirectory("trax-view-").FullName;
        try
        {
            var start = new ProcessStartInfo("node")
            {
                WorkingDirectory = FixturePaths.FolderRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (
                var arg in new[]
                {
                    "ts/tools/generate-twin.mjs",
                    "--ir",
                    FixturePaths.IrFile(machine),
                    "--engine-src",
                    Path.Combine(FixturePaths.TypeScriptEngineRoot, "src"),
                    "--out-dir",
                    outDir,
                    "--lang",
                    "csharp",
                    "--namespace",
                    Namespace,
                }
            )
                start.ArgumentList.Add(arg);
            using var process = Process.Start(start)!;
            var stderr = process.StandardError.ReadToEndAsync();
            // The test owns its wait (ADR 0014): the generator answers well within it, or the test fails.
            if (!process.WaitForExit(NodeTimeout))
            {
                process.Kill(entireProcessTree: true);
                Assert.Fail($"generate-twin.mjs did not finish within {NodeTimeout}.");
            }
            process.ExitCode.Should().Be(0, stderr.GetAwaiter().GetResult());

            var committed = Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "Machines", $"{file}.g.cs");
            File.ReadAllText(Path.Combine(outDir, $"{file}.g.cs"))
                .Should()
                .Be(File.ReadAllText(committed), $"{file}.g.cs is stale: run generate-twin.mjs --lang csharp for {machine}");
        }
        finally
        {
            Directory.Delete(outDir, recursive: true);
        }
    }
}
