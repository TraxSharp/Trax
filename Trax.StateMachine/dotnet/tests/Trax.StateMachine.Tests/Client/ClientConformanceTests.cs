using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Trax.Core.Extensions;
using Trax.StateMachine.Tests.Helpers;

namespace Trax.StateMachine.Tests.Client;

/// <summary>
/// The C# client through the client conformance kit, the same transcripts and the same fake host the TypeScript client
/// is checked with (<c>Trax.StateMachine/conformance/client/PROTOCOL.md</c>). The runner is started with <c>--stdio</c>
/// and this process answers its requests, each when it settles.
/// </summary>
[Category(Categories.CrossEngine)]
public partial class ClientConformanceTests
{
    private static readonly TimeSpan RunnerTimeout = TimeSpan.FromMinutes(2);

    [Test]
    public async Task The_CSharp_client_passes_every_transcript_of_the_kit()
    {
        var testee = new ClientTestee();
        await using var testeeDisposal = testee.ConfigureAwait(false);
        var start = new ProcessStartInfo(
            "node",
            $"conformance/client/run.mjs --name {ClientTestee.Client} --stdio"
        )
        {
            WorkingDirectory = FixturePaths.FolderRoot,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var process = Process.Start(start).NotNull();
        var stderr = process.StandardError.ReadToEndAsync();
        var writing = new SemaphoreSlim(1, 1);
        var answers = new List<Task>();

        // Each request is started as it arrives, so two sends are sent in the order the runner sent them, and answered
        // when it settles: a send waits on the host while the runner goes on.
        var conversation = Task.Run(async () =>
        {
            while (
                await process.StandardOutput.ReadLineAsync().ConfigureAwait(false) is { } request
            )
            {
                var answering = testee.AnswerAsync(request);
                answers.Add(
                    Task.Run(async () =>
                    {
                        var answer = await answering.ConfigureAwait(false);
                        await writing.WaitAsync().ConfigureAwait(false);
                        try
                        {
                            await process
                                .StandardInput.WriteLineAsync(answer)
                                .ConfigureAwait(false);
                            await process.StandardInput.FlushAsync().ConfigureAwait(false);
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
        // Skipped only where PROTOCOL.md's allowlist grants the C# client a lack, and excused only where its list of
        // expected failures names the transcript.
        var expected = await ExpectedFailures().ConfigureAwait(false);
        var skipped = await SkippedByTheAllowlist().ConfigureAwait(false);
        report
            .Should()
            .MatchRegex(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $@"([1-9]\d*) of \1 transcripts passed, {skipped} skipped, {expected} expected failures?\."
                )
            );
    }

    // The transcripts expected-failures/dotnet.json names, by case or by prefix; none when it is absent.
    private static async Task<int> ExpectedFailures()
    {
        var clientKit = Path.Combine(FixturePaths.ConformanceRoot, "client");
        var list = Path.Combine(clientKit, "expected-failures", $"{ClientTestee.Client}.json");
        if (!File.Exists(list))
            return 0;
        var entries = JsonNode
            .Parse(await File.ReadAllTextAsync(list).ConfigureAwait(false))
            .NotNull()
            .AsArray();
        return Directory
            .GetFiles(Path.Combine(clientKit, "transcripts"), "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Count(name =>
                entries.Any(e =>
                    string.Equals((string?)e.NotNull()["case"], name, StringComparison.Ordinal)
                    || (
                        e["prefix"] is { } prefix
                        && name.NotNull()
                            .StartsWith(((string?)prefix).NotNull(), StringComparison.Ordinal)
                    )
                )
            );
    }

    // A transcript may hold a context as deep as a stored snapshot nests, inside the answer that carries it.
    private static readonly JsonDocumentOptions Transcript = new() { MaxDepth = 256 };

    // The transcripts that need a capability the allowlist in PROTOCOL.md grants the C# client a lack of.
    private static async Task<int> SkippedByTheAllowlist()
    {
        var clientKit = Path.Combine(FixturePaths.ConformanceRoot, "client");
        var protocol = await File.ReadAllTextAsync(Path.Combine(clientKit, "PROTOCOL.md"))
            .ConfigureAwait(false);
        var block = LacksBlock.Match(protocol);
        block.Success.Should().BeTrue("PROTOCOL.md has a `json lacks` block");
        var lacked = JsonNode
            .Parse(block.Groups["lacks"].Value)
            .NotNull()
            .AsArray()
            .Where(e =>
                string.Equals(
                    (string?)e.NotNull()["client"],
                    ClientTestee.Client,
                    StringComparison.Ordinal
                )
            )
            .Select(e => ((string?)e.NotNull()["capability"]).NotNull())
            .ToHashSet(StringComparer.Ordinal);
        var skipped = 0;
        foreach (var path in Directory.GetFiles(Path.Combine(clientKit, "transcripts"), "*.json"))
        {
            var needs =
                JsonNode
                    .Parse(
                        await File.ReadAllTextAsync(path).ConfigureAwait(false),
                        documentOptions: Transcript
                    )
                    .NotNull()["needs"]
                    ?.AsArray()
                ?? [];
            if (needs.Any(n => lacked.Contains(((string?)n).NotNull())))
                skipped++;
        }
        return skipped;
    }

    [GeneratedRegex(
        @"^```json lacks\n(?<lacks>.*?)^```$",
        RegexOptions.Multiline | RegexOptions.Singleline,
        matchTimeoutMilliseconds: 1000
    )]
    private static partial Regex LacksBlock { get; }
}
