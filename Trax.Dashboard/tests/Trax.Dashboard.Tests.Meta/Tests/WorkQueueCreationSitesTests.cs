using System.Text.RegularExpressions;
using FluentAssertions;
using Trax.Dashboard.Tests.Meta.Infrastructure;

namespace Trax.Dashboard.Tests.Meta.Tests;

/// <summary>
/// The dashboard never builds a work queue row itself. Queueing and re-queueing go through
/// <c>IOperationsService</c>, which enqueues through the mediator so the train's <c>OnQueue</c>
/// hook, its subject key and the input cap apply; per-train authorization does not, because the
/// dashboard enqueues as the admin surface its host gates. The re-queue button once wrote the
/// row directly and skipped all of it. Guards
/// Trax.Docs/adr/0017-a-callers-enqueue-goes-through-the-mediator.md.
/// </summary>
[TestFixture]
[Property("adr", "Trax.Docs/adr/0017-a-callers-enqueue-goes-through-the-mediator.md")]
public class WorkQueueCreationSitesTests
{
    private static readonly Regex DirectCreate = new(
        @"\bWorkQueue\.Create\s*\(",
        RegexOptions.Compiled
    );

    [Test]
    public void No_component_builds_a_work_queue_row_directly()
    {
        var src = RepoRoot.Combine("src");
        var s = Path.DirectorySeparatorChar;

        Directory
            .EnumerateFiles(src, "*.*", SearchOption.AllDirectories)
            .Where(f =>
                f.EndsWith(".cs", StringComparison.Ordinal)
                || f.EndsWith(".razor", StringComparison.Ordinal)
            )
            .Where(f => !f.Contains($"{s}bin{s}") && !f.Contains($"{s}obj{s}"))
            .Where(f => DirectCreate.IsMatch(File.ReadAllText(f)))
            .Select(RepoRoot.Relative)
            .Should()
            .BeEmpty(
                "an enqueue from the dashboard is a caller's enqueue and must go through "
                    + "IOperationsService. See "
                    + "Trax.Docs/adr/0017-a-callers-enqueue-goes-through-the-mediator.md"
            );
    }
}
