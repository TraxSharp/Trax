using System.ComponentModel;
using System.Diagnostics;
using AwesomeAssertions;
using Trax.Cli.Clients;

namespace Trax.Cli.Tests.IntegrationTests.OperationsConformance;

/// <summary>
/// The operations clients <c>trax client --operations</c> writes, held to one transport and error behaviour by the
/// operations kit (<c>Trax.StateMachine/conformance/operations/PROTOCOL.md</c>). The TypeScript client generated for the
/// kit's fixture is committed beside its testee in <c>Trax.StateMachine/ts/tools/conformance/operations/client/</c>, and
/// the C# one in <c>Generated/</c> here, compiled into this project. These tests check both are what the CLI generates
/// today, and run the C# one through the kit with the runner started as <c>--stdio</c>. The TypeScript one runs through
/// the kit in <c>Trax.StateMachine/ts</c>'s vitest suite.
/// </summary>
public class OperationsConformanceTests
{
    /// <summary>The namespace the C# client is generated in.</summary>
    public const string Namespace =
        "Trax.Cli.Tests.IntegrationTests.OperationsConformance.Generated";

    private static readonly TimeSpan RunnerTimeout = TimeSpan.FromMinutes(2);

    private const string Regenerate =
        "Regenerate both clients as conformance/operations/PROTOCOL.md says (\"The fixture\").";

    [Test]
    public void The_committed_clients_and_registered_documents_are_what_the_cli_generates()
    {
        var root = RepositoryRoot();
        var fixture = Path.Combine(
            root,
            "Trax.StateMachine",
            "conformance",
            "operations",
            "fixture"
        );
        var options = new OperationsGenerateOptions(
            File.ReadAllText(Path.Combine(fixture, "operations.graphql")),
            Directory
                .GetFiles(Path.Combine(fixture, "documents"), "*.graphql")
                .Select(path => Path.GetRelativePath(fixture, path).Replace('\\', '/'))
                .Order(StringComparer.Ordinal)
                .Select(source => new ClientDocumentFile(
                    source,
                    File.ReadAllText(Path.Combine(fixture, source))
                ))
                .ToList()
        );

        ClientGenerator
            .Check(
                options,
                Path.Combine(
                    root,
                    "Trax.StateMachine",
                    "ts",
                    "tools",
                    "conformance",
                    "operations",
                    "client"
                )
            )
            .Select(d => $"{d.Status} {d.Path}")
            .Should()
            .BeEmpty($"the TypeScript client is what the fixture generates. {Regenerate}");
        ClientGenerator
            .CheckCSharp(
                options,
                Namespace,
                Path.Combine(
                    root,
                    "Trax.Cli",
                    "tests",
                    "Trax.Cli.Tests",
                    "IntegrationTests",
                    "OperationsConformance",
                    "Generated"
                )
            )
            .Select(d => $"{d.Status} {d.Path}")
            .Should()
            .BeEmpty($"the C# client is what the fixture generates. {Regenerate}");
        File.ReadAllText(Path.Combine(fixture, ClientGenerator.ConsoleDocumentsFileName))
            .Should()
            .Be(
                ClientGenerator
                    .Render(options)
                    .Single(f => f.Path == ClientGenerator.ConsoleDocumentsFileName)
                    .Text,
                $"the host registers the documents the clients send. {Regenerate}"
            );
    }

    [Test]
    public async Task The_CSharp_operations_client_passes_every_transcript_of_the_kit()
    {
        var root = RepositoryRoot();
        var start = new ProcessStartInfo("node", "conformance/operations/run.mjs --stdio")
        {
            WorkingDirectory = Path.Combine(root, "Trax.StateMachine"),
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        Process process;
        try
        {
            process = Process.Start(start)!;
        }
        catch (Win32Exception)
        {
            NodePrerequisite.Missing("node is not on PATH; the operations kit's runner needs it.");
            return;
        }
        using var _ = process;
        await using var testee = new OperationsTestee();
        var stderr = process.StandardError.ReadToEndAsync();
        var writing = new SemaphoreSlim(1, 1);

        // Each request is started as it arrives and answered when it settles: a send waits on the host while the runner
        // goes on.
        var conversation = Task.Run(async () =>
        {
            var answers = new List<Task>();
            while (await process.StandardOutput.ReadLineAsync() is { } request)
            {
                var answering = testee.AnswerAsync(request);
                answers.Add(
                    Task.Run(async () =>
                    {
                        var answer = await answering;
                        await writing.WaitAsync();
                        try
                        {
                            await process.StandardInput.WriteLineAsync(answer);
                            await process.StandardInput.FlushAsync();
                        }
                        finally
                        {
                            writing.Release();
                        }
                    })
                );
            }
        });

        // The test owns its wait (ADR 0014): the runner finishes well within it, or the test fails.
        using var timeout = new CancellationTokenSource(RunnerTimeout);
        try
        {
            await conversation.WaitAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail(
                $"conformance/operations/run.mjs did not finish within {RunnerTimeout}. It reported:\n{await stderr}"
            );
        }
        var report = await stderr;

        process.ExitCode.Should().Be(0, report);
        report.Should().MatchRegex(@"([1-9]\d*) of \1 transcripts passed\.");
    }

    // The repository's root: the directory above the test binary that holds global.json and Trax.StateMachine.
    private static string RepositoryRoot()
    {
        for (
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            dir is not null;
            dir = dir.Parent
        )
            if (
                File.Exists(Path.Combine(dir.FullName, "global.json"))
                && Directory.Exists(Path.Combine(dir.FullName, "Trax.StateMachine", "conformance"))
            )
                return dir.FullName;
        Assert.Ignore("The repository is not checked out around this build (an isolated build).");
        return "";
    }
}
