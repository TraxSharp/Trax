using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// Integration tests for the manifest-group surface of <see cref="IOperationsService"/>:
/// patch-style updates and the 1-hop cross-group dependency graph. These are the source
/// of truth for both the dashboard's settings save / DAG render and the GraphQL
/// <c>operations.manifestGroups.*</c> namespace.
/// </summary>
[TestFixture]
public class OperationsServiceManifestGroupTests : TestSetup
{
    private IOperationsService _operations = null!;

    [SetUp]
    public void GetService()
    {
        _operations = Scope.ServiceProvider.GetRequiredService<IOperationsService>();
    }

    private async Task<Manifest> SeedManifest(long groupId, long? dependsOn = null)
    {
        var m = Manifest.Create(new CreateManifest { Name = typeof(ISchedulerTestTrain) });
        m.ManifestGroupId = groupId;
        m.DependsOnManifestId = dependsOn;
        await DataContext.Track(m);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();
        return m;
    }

    #region UpdateManifestGroupAsync

    [Test]
    public async Task UpdateManifestGroupAsync_AllFields_Updated()
    {
        var group = await CreateAndSaveManifestGroup(
            DataContext,
            "g",
            maxActiveJobs: 1,
            priority: 0,
            isEnabled: true
        );

        var result = await _operations.UpdateManifestGroupAsync(
            group.Id,
            new UpdateManifestGroupInput(MaxActiveJobs: 5, Priority: 7, IsEnabled: false),
            CancellationToken.None
        );

        result.Success.Should().BeTrue();
        result.Count.Should().Be(3);

        DataContext.Reset();
        var fresh = DataContext.ManifestGroups.Single();
        fresh.MaxActiveJobs.Should().Be(5);
        fresh.Priority.Should().Be(7);
        fresh.IsEnabled.Should().BeFalse();
        fresh.UpdatedAt.Should().BeAfter(group.CreatedAt);
    }

    [Test]
    public async Task UpdateManifestGroupAsync_PartialPatch_OnlyTouchedFieldsChange()
    {
        var group = await CreateAndSaveManifestGroup(
            DataContext,
            "g",
            maxActiveJobs: 3,
            priority: 1,
            isEnabled: true
        );

        var result = await _operations.UpdateManifestGroupAsync(
            group.Id,
            new UpdateManifestGroupInput(Priority: 9),
            CancellationToken.None
        );

        result.Success.Should().BeTrue();
        result.Count.Should().Be(1);

        DataContext.Reset();
        var fresh = DataContext.ManifestGroups.Single();
        fresh.Priority.Should().Be(9);
        fresh.MaxActiveJobs.Should().Be(3);
        fresh.IsEnabled.Should().BeTrue();
    }

    [Test]
    public async Task UpdateManifestGroupAsync_ClearMaxActiveJobs_SetsNull()
    {
        var group = await CreateAndSaveManifestGroup(DataContext, "g", maxActiveJobs: 4);

        var result = await _operations.UpdateManifestGroupAsync(
            group.Id,
            new UpdateManifestGroupInput(ClearMaxActiveJobs: true),
            CancellationToken.None
        );

        result.Success.Should().BeTrue();
        result.Count.Should().Be(1);

        DataContext.Reset();
        DataContext.ManifestGroups.Single().MaxActiveJobs.Should().BeNull();
    }

    [Test]
    public async Task UpdateManifestGroupAsync_ClearMaxActiveJobs_AlreadyNull_NoOp()
    {
        // MaxActiveJobs already null → ClearMaxActiveJobs should not register a change
        // and UpdatedAt should remain untouched.
        var group = await CreateAndSaveManifestGroup(DataContext, "g", maxActiveJobs: null);
        var initialUpdated = group.UpdatedAt;

        var result = await _operations.UpdateManifestGroupAsync(
            group.Id,
            new UpdateManifestGroupInput(ClearMaxActiveJobs: true),
            CancellationToken.None
        );

        result.Success.Should().BeTrue();
        result.Count.Should().Be(0);

        DataContext.Reset();
        DataContext
            .ManifestGroups.Single()
            .UpdatedAt.Should()
            .BeCloseTo(initialUpdated, TimeSpan.FromMilliseconds(1));
    }

    [Test]
    public async Task UpdateManifestGroupAsync_ClearTakesPrecedenceOverMaxValue()
    {
        // If both are passed, Clear wins — defensive against client bugs that set both.
        var group = await CreateAndSaveManifestGroup(DataContext, "g", maxActiveJobs: 4);

        var result = await _operations.UpdateManifestGroupAsync(
            group.Id,
            new UpdateManifestGroupInput(MaxActiveJobs: 10, ClearMaxActiveJobs: true),
            CancellationToken.None
        );

        result.Success.Should().BeTrue();

        DataContext.Reset();
        DataContext.ManifestGroups.Single().MaxActiveJobs.Should().BeNull();
    }

    [Test]
    public async Task UpdateManifestGroupAsync_NoChanges_ReturnsZeroCount()
    {
        var group = await CreateAndSaveManifestGroup(
            DataContext,
            "g",
            maxActiveJobs: 5,
            priority: 2,
            isEnabled: true
        );
        var initialUpdated = group.UpdatedAt;

        var result = await _operations.UpdateManifestGroupAsync(
            group.Id,
            new UpdateManifestGroupInput(MaxActiveJobs: 5, Priority: 2, IsEnabled: true),
            CancellationToken.None
        );

        result.Success.Should().BeTrue();
        result.Count.Should().Be(0);

        DataContext.Reset();
        DataContext
            .ManifestGroups.Single()
            .UpdatedAt.Should()
            .BeCloseTo(initialUpdated, TimeSpan.FromMilliseconds(1));
    }

    [Test]
    public async Task UpdateManifestGroupAsync_EmptyPatch_NoOp()
    {
        var group = await CreateAndSaveManifestGroup(DataContext, "g");
        var initialUpdated = group.UpdatedAt;

        var result = await _operations.UpdateManifestGroupAsync(
            group.Id,
            new UpdateManifestGroupInput(),
            CancellationToken.None
        );

        result.Success.Should().BeTrue();
        result.Count.Should().Be(0);
        DataContext.Reset();
        DataContext
            .ManifestGroups.Single()
            .UpdatedAt.Should()
            .BeCloseTo(initialUpdated, TimeSpan.FromMilliseconds(1));
    }

    [Test]
    public async Task UpdateManifestGroupAsync_MissingId_ReturnsFailure()
    {
        var result = await _operations.UpdateManifestGroupAsync(
            99999,
            new UpdateManifestGroupInput(Priority: 5),
            CancellationToken.None
        );

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("not found");
    }

    #endregion

    #region GetManifestGroupDependencyGraphAsync

    [Test]
    public async Task GetGraph_MissingGroup_ReturnsNull()
    {
        var graph = await _operations.GetManifestGroupDependencyGraphAsync(
            99999,
            CancellationToken.None
        );

        graph.Should().BeNull();
    }

    [Test]
    public async Task GetGraph_EmptyGroup_ReturnsSingleFocalNode()
    {
        var group = await CreateAndSaveManifestGroup(DataContext, "lonely");

        var graph = await _operations.GetManifestGroupDependencyGraphAsync(
            group.Id,
            CancellationToken.None
        );

        graph.Should().NotBeNull();
        graph!.Nodes.Should().ContainSingle(n => n.IsHighlighted && n.Id == group.Id);
        graph.Edges.Should().BeEmpty();
    }

    [Test]
    public async Task GetGraph_GroupWithoutCrossGroupDeps_StillReturnsFocalNode()
    {
        var group = await CreateAndSaveManifestGroup(DataContext, "solo");
        // Two manifests in the same group, one depends on the other (intra-group, not cross-group).
        var parent = await SeedManifest(group.Id);
        await SeedManifest(group.Id, dependsOn: parent.Id);

        var graph = await _operations.GetManifestGroupDependencyGraphAsync(
            group.Id,
            CancellationToken.None
        );

        graph.Should().NotBeNull();
        graph!.Nodes.Should().ContainSingle(n => n.Id == group.Id && n.IsHighlighted);
        graph.Edges.Should().BeEmpty();
    }

    [Test]
    public async Task GetGraph_UpstreamDependency_NodeAndEdgeReturned()
    {
        var upstream = await CreateAndSaveManifestGroup(DataContext, "upstream");
        var focal = await CreateAndSaveManifestGroup(DataContext, "focal");
        var upstreamManifest = await SeedManifest(upstream.Id);
        await SeedManifest(focal.Id, dependsOn: upstreamManifest.Id);

        var graph = await _operations.GetManifestGroupDependencyGraphAsync(
            focal.Id,
            CancellationToken.None
        );

        graph.Should().NotBeNull();
        graph!.Nodes.Should().HaveCount(2);
        graph.Nodes.Should().ContainSingle(n => n.Id == focal.Id && n.IsHighlighted);
        graph.Nodes.Should().ContainSingle(n => n.Id == upstream.Id && !n.IsHighlighted);
        graph.Edges.Should().ContainSingle(e => e.FromId == upstream.Id && e.ToId == focal.Id);
    }

    [Test]
    public async Task GetGraph_DownstreamDependency_NodeAndEdgeReturned()
    {
        var focal = await CreateAndSaveManifestGroup(DataContext, "focal");
        var downstream = await CreateAndSaveManifestGroup(DataContext, "downstream");
        var focalManifest = await SeedManifest(focal.Id);
        await SeedManifest(downstream.Id, dependsOn: focalManifest.Id);

        var graph = await _operations.GetManifestGroupDependencyGraphAsync(
            focal.Id,
            CancellationToken.None
        );

        graph.Should().NotBeNull();
        graph!.Nodes.Should().HaveCount(2);
        graph.Edges.Should().ContainSingle(e => e.FromId == focal.Id && e.ToId == downstream.Id);
    }

    [Test]
    public async Task GetGraph_BothDirections_FullNeighborhoodReturned()
    {
        var upstream = await CreateAndSaveManifestGroup(DataContext, "u");
        var focal = await CreateAndSaveManifestGroup(DataContext, "f");
        var downstream = await CreateAndSaveManifestGroup(DataContext, "d");

        var upM = await SeedManifest(upstream.Id);
        var focalM = await SeedManifest(focal.Id, dependsOn: upM.Id);
        await SeedManifest(downstream.Id, dependsOn: focalM.Id);

        var graph = await _operations.GetManifestGroupDependencyGraphAsync(
            focal.Id,
            CancellationToken.None
        );

        graph.Should().NotBeNull();
        graph!
            .Nodes.Select(n => n.Id)
            .Should()
            .BeEquivalentTo(new[] { upstream.Id, focal.Id, downstream.Id });
        graph.Edges.Should().HaveCount(2);
        graph.Edges.Should().ContainSingle(e => e.FromId == upstream.Id && e.ToId == focal.Id);
        graph.Edges.Should().ContainSingle(e => e.FromId == focal.Id && e.ToId == downstream.Id);
    }

    [Test]
    public async Task GetGraph_DuplicateCrossGroupEdges_Deduplicated()
    {
        // Multiple manifests in focal depending on multiple manifests in upstream
        // should still produce a single edge.
        var upstream = await CreateAndSaveManifestGroup(DataContext, "u");
        var focal = await CreateAndSaveManifestGroup(DataContext, "f");
        var u1 = await SeedManifest(upstream.Id);
        var u2 = await SeedManifest(upstream.Id);
        await SeedManifest(focal.Id, dependsOn: u1.Id);
        await SeedManifest(focal.Id, dependsOn: u2.Id);

        var graph = await _operations.GetManifestGroupDependencyGraphAsync(
            focal.Id,
            CancellationToken.None
        );

        graph.Should().NotBeNull();
        graph!.Edges.Should().HaveCount(1);
    }

    #endregion

    #region GetGlobalManifestGroupGraphAsync

    [Test]
    public async Task GetGlobalGraph_ReturnsEveryGroupAndCrossGroupEdges_NothingHighlighted()
    {
        var a = await CreateAndSaveManifestGroup(DataContext, "a");
        var b = await CreateAndSaveManifestGroup(DataContext, "b");
        var c = await CreateAndSaveManifestGroup(DataContext, "c");
        var aM = await SeedManifest(a.Id);
        var bM = await SeedManifest(b.Id, dependsOn: aM.Id); // a → b
        await SeedManifest(c.Id, dependsOn: bM.Id); // b → c

        var graph = await _operations.GetGlobalManifestGroupGraphAsync(CancellationToken.None);

        graph.Nodes.Select(n => n.Id).Should().BeEquivalentTo(new[] { a.Id, b.Id, c.Id });
        graph.Nodes.Should().OnlyContain(n => !n.IsHighlighted);
        graph.Edges.Should().HaveCount(2);
        graph.Edges.Should().ContainSingle(e => e.FromId == a.Id && e.ToId == b.Id);
        graph.Edges.Should().ContainSingle(e => e.FromId == b.Id && e.ToId == c.Id);
    }

    [Test]
    public async Task GetGlobalGraph_OmitsIntraGroupDependencies()
    {
        var g = await CreateAndSaveManifestGroup(DataContext, "solo");
        var first = await SeedManifest(g.Id);
        await SeedManifest(g.Id, dependsOn: first.Id); // same-group dependency

        var graph = await _operations.GetGlobalManifestGroupGraphAsync(CancellationToken.None);

        graph.Nodes.Should().ContainSingle(n => n.Id == g.Id);
        graph.Edges.Should().BeEmpty("a dependency within one group is not a cross-group edge");
    }

    #endregion
}
