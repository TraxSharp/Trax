using System.Diagnostics;
using AwesomeAssertions;
using Trax.Cli.Commands;
using Trax.Cli.Deploy;
using Trax.Cli.Deploy.Aws;
using Trax.Cli.Deploy.Kubernetes;
using Trax.Effect.Configuration.Deploy;

namespace Trax.Cli.Tests.UnitTests;

/// <summary>
/// Every image trax deploy builds comes from a locked restore of its project and a publish that does not restore
/// again, so a deploy never rewrites a committed packages.lock.json or builds from packages it does not name.
/// </summary>
public class LockedPublishTests
{
    private const string Project = "/src/Orders/Orders.csproj";

    private static IEnumerable<TestCaseData> Publishes()
    {
        yield return new TestCaseData(
            DotnetContainerPublisher.StartInfo(Project, "ghcr.io/acme", "orders-hub", "t")
        ).SetName("kubernetes, to a registry");
        yield return new TestCaseData(
            DotnetContainerPublisher.StartInfo(Project, registry: null, "orders-hub", "t", "podman")
        ).SetName("kubernetes, into podman");
        yield return new TestCaseData(
            DotnetImagePublisher.StartInfo(
                Project,
                "123.dkr.ecr.us-west-2.amazonaws.com/trax/orders/hub",
                "t",
                new RegistryLogin("123.dkr.ecr.us-west-2.amazonaws.com", "AWS", "token")
            )
        ).SetName("aws");
        yield return new TestCaseData(DeployCommand.ComposeImage(Project, "orders-hub")).SetName(
            "compose --build"
        );
    }

    [TestCaseSource(nameof(Publishes))]
    public void Every_image_publishes_without_restoring(ProcessStartInfo publish) =>
        publish
            .ArgumentList.Should()
            .StartWith(["publish", Project, "-c", "Release", "--no-restore"]);

    [TestCaseSource(nameof(Publishes))]
    public void No_image_is_published_for_a_runtime_identifier(ProcessStartInfo publish)
    {
        // A RID-specific publish needs a RID-specific restore, which the committed lockfiles do not hold.
        publish.ArgumentList.Should().NotContain(["-r", "--runtime", "--os", "--arch"]);
        publish
            .ArgumentList.Should()
            .NotContain(a => a.StartsWith("-p:RuntimeIdentifier", StringComparison.Ordinal));
    }

    [Test]
    public void The_aws_and_kubernetes_images_are_made_for_linux_x64()
    {
        DotnetContainerPublisher
            .StartInfo(Project, "ghcr.io/acme", "orders-hub", "t")
            .ArgumentList.Should()
            .Contain("-p:ContainerRuntimeIdentifier=linux-x64");
        DotnetImagePublisher
            .StartInfo(
                Project,
                "123.dkr.ecr.us-west-2.amazonaws.com/trax/orders/hub",
                "t",
                new RegistryLogin("123.dkr.ecr.us-west-2.amazonaws.com", "AWS", "token")
            )
            .ArgumentList.Should()
            .Contain("-p:ContainerRuntimeIdentifier=linux-x64");
    }

    [Test]
    public void The_restore_is_locked_and_sets_no_runtime_on_the_projects_it_references() =>
        LockedPublish
            .Restore(Project)
            .ArgumentList.Should()
            .Equal(
                ["restore", Project, "--locked-mode"],
                "-r reaches every referenced project, whose lockfiles hold no runtime section"
            );

    [Test]
    public async Task A_restore_that_fails_stops_before_the_publish()
    {
        var ran = new List<string>();

        var act = () =>
            LockedPublish.RunAsync(
                LockedPublish.Restore(Project),
                LockedPublish.Publish(Project),
                "orders-hub:t",
                psi =>
                {
                    ran.Add(psi.ArgumentList[0]);
                    return Task.FromResult(
                        (1, "error NU1004: The packages lock file is inconsistent", "")
                    );
                }
            );

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage(
            "Restoring Orders.csproj*does not rewrite lockfiles*--force-evaluate*"
        );
        ran.Should().Equal("restore");
    }
}
