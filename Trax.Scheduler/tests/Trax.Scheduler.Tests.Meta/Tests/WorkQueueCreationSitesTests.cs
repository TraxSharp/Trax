using System.Text.RegularExpressions;
using FluentAssertions;

namespace Trax.Scheduler.Tests.Meta.Tests;

/// <summary>
/// A work queue row is written directly only by system-initiated enqueues (the ManifestManager,
/// and dormant dependents a parent train activates with input its own code chose) and by
/// <c>ITraxScheduler</c>'s actions on existing manifests (trigger, group trigger, dead-letter
/// requeue). Everything a caller enqueues goes through <c>ITrainExecutionService.QueueAsync</c>,
/// which applies the train's authorization, its <c>OnQueue</c> hook and its subject key.
/// Guards Trax.Docs/adr/0017-a-callers-enqueue-goes-through-the-mediator.md.
/// </summary>
[TestFixture]
[Property("adr", "Trax.Docs/adr/0017-a-callers-enqueue-goes-through-the-mediator.md")]
public class WorkQueueCreationSitesTests
{
    private static readonly Regex DirectCreate = new(
        @"\bWorkQueue\.Create\s*\(",
        RegexOptions.Compiled
    );

    /// <summary>
    /// The sites allowed to build a row themselves: none enqueues a train and input a caller
    /// chose at request time.
    /// </summary>
    /// <remarks>
    /// The allow-list is per file, not per method: every <c>WorkQueue.Create</c> in a listed file
    /// passes unchecked, including one added there later, so a new one in these files has to be
    /// reviewed by hand.
    /// </remarks>
    private static readonly HashSet<string> ManifestSites = new(StringComparer.Ordinal)
    {
        // The ManifestManager's scheduled enqueue: no caller at all.
        "src/Trax.Scheduler/Trains/ManifestManager/Junctions/CreateWorkQueueEntriesJunction.cs",
        // Dormant dependents activated by their parent's run: no caller at all.
        "src/Trax.Scheduler/Services/DormantDependentContext/DormantDependentContext.cs",
        // Triggering a manifest (or a group's manifests) early and re-queueing a dead letter: the
        // caller picks which manifest, never the train or the input. ITraxScheduler is public
        // host API, reached from the admin surfaces and equally from a host's own background
        // code, where there is no user to authorize (docs/0017).
        "src/Trax.Scheduler/Services/TraxScheduler/TraxScheduler.cs",
    };

    [Test]
    public void Only_manifest_enqueues_build_a_work_queue_row_directly()
    {
        var src = RepoRoot.Combine("src");

        var offenders = Directory
            .EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f =>
                !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
            )
            .Where(f =>
                !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
            )
            .Select(f => RepoRoot.Relative(f).Replace('\\', '/'))
            .Where(rel => !ManifestSites.Contains(rel))
            .Where(rel => DirectCreate.IsMatch(File.ReadAllText(RepoRoot.Combine(rel))))
            .ToList();

        offenders
            .Should()
            .BeEmpty(
                "a caller's enqueue must go through ITrainExecutionService.QueueAsync so the "
                    + "train's authorization, OnQueue hook and subject key apply; a hand-built "
                    + "row skips all three. See "
                    + "Trax.Docs/adr/0017-a-callers-enqueue-goes-through-the-mediator.md"
            );
    }

    [Test]
    public void Every_listed_manifest_site_still_exists()
    {
        ManifestSites
            .Where(rel => !File.Exists(RepoRoot.Combine(rel)))
            .Should()
            .BeEmpty(
                "a stale allow-list entry would let a new file at that path skip the check. See "
                    + "Trax.Docs/adr/0017-a-callers-enqueue-goes-through-the-mediator.md"
            );
    }
}
