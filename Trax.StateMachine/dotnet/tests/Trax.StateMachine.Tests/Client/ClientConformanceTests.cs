using System.Diagnostics;
using AwesomeAssertions;
using Trax.StateMachine.Tests.Helpers;

namespace Trax.StateMachine.Tests.Client;

/// <summary>
/// The C# client through the client conformance kit, the same transcripts and the same fake host the TypeScript client
/// is checked with (<c>Trax.StateMachine/conformance/client/PROTOCOL.md</c>). The runner is started with <c>--stdio</c>
/// and this process answers its requests, each when it settles.
/// </summary>
[Category(Categories.CrossEngine)]
public class ClientConformanceTests
{
    private static readonly TimeSpan RunnerTimeout = TimeSpan.FromMinutes(2);

    [Test]
    public async Task The_CSharp_client_passes_every_transcript_of_the_kit()
    {
        await using var testee = new ClientTestee();
        var start = new ProcessStartInfo("node", "conformance/client/run.mjs --stdio")
        {
            WorkingDirectory = FixturePaths.FolderRoot,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var process = Process.Start(start)!;
        var stderr = process.StandardError.ReadToEndAsync();
        var writing = new SemaphoreSlim(1, 1);
        var answers = new List<Task>();

        // Each request is started as it arrives, so two sends are sent in the order the runner sent them, and answered
        // when it settles: a send waits on the host while the runner goes on.
        var conversation = Task.Run(async () =>
        {
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
                $"conformance/client/run.mjs did not finish within {RunnerTimeout}. It reported:\n{await stderr}"
            );
        }
        var report = await stderr;

        process.ExitCode.Should().Be(0, report);
        report.Should().MatchRegex(@"([1-9]\d*) of \1 transcripts passed\.");
    }
}
