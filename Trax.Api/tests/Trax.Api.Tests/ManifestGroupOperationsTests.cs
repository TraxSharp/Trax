using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using Trax.Api.GraphQL.Mutations;
using Trax.Api.GraphQL.Queries;
using Trax.Scheduler.Services.Operations;

namespace Trax.Api.Tests;

/// <summary>
/// Pass-through tests for the GraphQL <c>operations.manifestGroups</c> namespace.
/// Behavioural tests for the underlying <see cref="IOperationsService"/> live in
/// <c>Trax.Scheduler.Tests.Integration.OperationsServiceManifestGroupTests</c>.
/// Here we only verify the GraphQL layer correctly forwards arguments and translates
/// <see cref="OperationResult"/> into the API <c>OperationResponse</c>.
/// </summary>
[TestFixture]
public class ManifestGroupOperationsTests
{
    [Test]
    public async Task UpdateManifestGroup_ForwardsToOperationsService_AndMapsSuccess()
    {
        var ops = Substitute.For<IOperationsService>();
        ops.UpdateManifestGroupAsync(
                Arg.Any<long>(),
                Arg.Any<UpdateManifestGroupInput>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new OperationResult(true, Id: 42, Count: 2, Message: "updated"));
        var mutations = new ManifestGroupMutations();
        var input = new UpdateManifestGroupInput(Priority: 5, IsEnabled: false);

        var response = await mutations.UpdateManifestGroup(42, input, ops, default);

        response.Success.Should().BeTrue();
        response.Count.Should().Be(2);
        response.Message.Should().Be("updated");
        await ops.Received(1).UpdateManifestGroupAsync(42, input, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UpdateManifestGroup_ForwardsToOperationsService_AndMapsFailure()
    {
        var ops = Substitute.For<IOperationsService>();
        ops.UpdateManifestGroupAsync(
                Arg.Any<long>(),
                Arg.Any<UpdateManifestGroupInput>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new OperationResult(false, Message: "Manifest group 999 not found."));
        var mutations = new ManifestGroupMutations();

        var response = await mutations.UpdateManifestGroup(
            999,
            new UpdateManifestGroupInput(Priority: 1),
            ops,
            default
        );

        response.Success.Should().BeFalse();
        response.Message.Should().Contain("not found");
    }

    [Test]
    public async Task GetGraph_ForwardsToOperationsService()
    {
        var ops = Substitute.For<IOperationsService>();
        var graph = new ManifestGroupDependencyGraph(
            new[] { new DependencyGraphNode(7, "focal", true) },
            Array.Empty<DependencyGraphEdge>()
        );
        ops.GetManifestGroupDependencyGraphAsync(7, Arg.Any<CancellationToken>()).Returns(graph);
        var queries = new ManifestGroupQueries();

        var result = await queries.GetGraph(7, ops, default);

        result.Should().BeSameAs(graph);
    }

    [Test]
    public async Task GetGraph_MissingGroup_ReturnsNull()
    {
        var ops = Substitute.For<IOperationsService>();
        ops.GetManifestGroupDependencyGraphAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns((ManifestGroupDependencyGraph?)null);
        var queries = new ManifestGroupQueries();

        var result = await queries.GetGraph(99999, ops, default);

        result.Should().BeNull();
    }

    [Test]
    public void OperationsQueries_ManifestGroupsNamespace_ReturnsNewInstance()
    {
        new OperationsQueries().ManifestGroups().Should().NotBeNull();
    }

    [Test]
    public void OperationsMutations_ManifestGroupsNamespace_ReturnsNewInstance()
    {
        new OperationsMutations().ManifestGroups().Should().NotBeNull();
    }
}
