using System.Text.RegularExpressions;
using FluentAssertions;
using Trax.Api.Tests.Meta.Infrastructure;

namespace Trax.Api.Tests.Meta.Tests;

/// <summary>
/// The API never builds a work queue row itself. Its enqueue mutations go through
/// <c>IOperationsService</c> and <c>ITrainExecutionService.QueueAsync</c>, which apply the
/// train's authorization, its <c>OnQueue</c> hook and its subject key; a hand-built row skips all
/// three. Guards Trax.Docs/adr/0017-a-callers-enqueue-goes-through-the-mediator.md.
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
    public void No_source_file_builds_a_work_queue_row_directly()
    {
        SourceFiles
            .CSharp("src")
            .Where(f => DirectCreate.IsMatch(File.ReadAllText(f)))
            .Select(RepoRoot.Relative)
            .Should()
            .BeEmpty(
                "an enqueue from the API is a caller's enqueue and must go through the mediator. "
                    + "See Trax.Docs/adr/0017-a-callers-enqueue-goes-through-the-mediator.md"
            );
    }
}
