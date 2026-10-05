namespace Trax.Samples.Tests.Meta.Tests;

/// <summary>
/// An AwesomeAssertions async assertion (<c>ThrowAsync</c>, <c>NotThrowAsync</c>,
/// <c>CompleteWithinAsync</c>, ...) returns a Task that carries the verdict. Called without
/// <c>await</c> from a <c>void</c> test, the Task is dropped, the assertion never runs, and the
/// test passes whatever the code under test does.
///
/// <para>The scan reads the whole file, not a line at a time: csharpier splits a long chain into
/// <c>act.Should()</c> and <c>.ThrowAsync&lt;X&gt;()</c> on separate lines, and the
/// <c>await</c> sits at the start of the statement, lines above the assertion.</para>
///
/// <para>Not ADR-enforcing: it checks how an unawaited Task behaves, not a choice between
/// alternatives.</para>
/// </summary>
[TestFixture]
public class AsyncAssertionsAreAwaitedTests
{
    private static readonly Regex AsyncAssertion = new(
        @"\.Should\(\)\s*\.\s*(ThrowAsync|ThrowExactlyAsync|NotThrowAsync|NotThrowAfterAsync|CompleteWithinAsync|NotCompleteWithinAsync)\b",
        RegexOptions.Compiled
    );

    private static readonly Regex AwaitedOrReturned = new(
        @"\b(await|return)\b",
        RegexOptions.Compiled
    );

    [Test]
    public void Every_async_assertion_in_the_tests_is_awaited()
    {
        var offenders = new List<string>();

        foreach (var file in SourceFiles.CSharp("tests"))
        {
            if (file.EndsWith("AsyncAssertionsAreAwaitedTests.cs", StringComparison.Ordinal))
                continue;

            foreach (var line in UnawaitedLines(File.ReadAllText(file)))
                offenders.Add($"{RepoRoot.Relative(file)}:{line}");
        }

        offenders
            .Should()
            .BeEmpty(
                "an async assertion that is not awaited is discarded, so the test passes even when "
                    + "the code under test does not throw. Make the test async Task and await it. "
                    + "Offenders:\n  "
                    + string.Join("\n  ", offenders)
            );
    }

    [Test]
    public void A_chain_split_across_lines_is_still_checked()
    {
        const string unawaited = """
            public void Refused()
            {
                act.Should()
                    .ThrowAsync<InvalidOperationException>()
                    .WithMessage("nope");
            }
            """;
        const string awaited = """
            public async Task Refused()
            {
                await act.Should()
                    .ThrowAsync<InvalidOperationException>()
                    .WithMessage("nope");
                var closed = (
                    await connect.Should().ThrowAsync<WebSocketClosedException>()
                ).Which;
            }
            """;

        UnawaitedLines(unawaited).Should().Equal([3], "the statement holds no await");
        UnawaitedLines(awaited).Should().BeEmpty("both statements await their assertion");
    }

    /// <summary>
    /// The 1-based lines of <paramref name="source"/> whose statement makes an async assertion
    /// without awaiting or returning it. The statement runs back from the assertion to the
    /// previous <c>;</c>, <c>{</c> or <c>}</c>.
    /// </summary>
    private static IEnumerable<int> UnawaitedLines(string source)
    {
        var stripped = SourceText.StripCommentsAndStrings(source.Replace("\r\n", "\n"));
        foreach (Match match in AsyncAssertion.Matches(stripped))
        {
            var start = stripped.LastIndexOfAny([';', '{', '}'], match.Index) + 1;
            var statement = stripped[start..match.Index];
            if (!AwaitedOrReturned.IsMatch(statement))
                yield return stripped[..match.Index].Count(c => c == '\n') + 1;
        }
    }
}
