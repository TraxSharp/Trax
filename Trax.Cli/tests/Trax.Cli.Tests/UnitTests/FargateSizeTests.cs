using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.Cli.Deploy;
using Trax.Cli.Deploy.Aws;
using Trax.Core.Extensions;

namespace Trax.Cli.Tests.UnitTests;

/// <summary>
/// A service's <c>memory</c> and <c>cpus</c> on aws become the cheapest Fargate pair that holds both, the task
/// definition and plan's cost say so, and a Trax container's root is read-only with a scratch <c>/tmp</c>, as on
/// compose and Kubernetes. AWS refuses what Fargate cannot apply: pids, and limits on services it does not run.
/// </summary>
public class FargateSizeTests
{
    [TestCase(null, null, 512, 1024)]
    [TestCase(512, null, 512, 1024)]
    [TestCase(1024, null, 512, 1024)]
    [TestCase(1536, null, 512, 2048)]
    [TestCase(4096, null, 512, 4096)]
    [TestCase(4097, null, 1024, 5120)]
    [TestCase(8192, null, 1024, 8192)]
    [TestCase(9000, null, 2048, 9216)]
    [TestCase(null, 0.25, 256, 1024)]
    [TestCase(null, 1.0, 1024, 2048)]
    [TestCase(null, 2.0, 2048, 4096)]
    [TestCase(2048, 0.1, 256, 2048)]
    [TestCase(122880, 16.0, 16384, 122880)]
    public void A_service_gets_the_cheapest_fargate_pair_that_holds_it(
        int? memory,
        double? cpus,
        int cpu,
        int mib
    )
    {
        var size = FargateSize.For(memory, cpus is { } c ? (decimal)c : null);

        size.Should().Be(new FargateSize(cpu, mib));
        FargateSize.Valid.Should().Contain(size, "Fargate accepts only its listed pairs");
    }

    [Test]
    public void A_size_no_fargate_task_holds_is_refused()
    {
        var act = () => FargateSize.For(130000, cpus: null);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*No Fargate task holds 130000 MiB*");
    }

    [Test]
    public void The_default_costs_what_plan_said_before_sizes_were_read()
    {
        decimal.Round(FargateSize.Default.Monthly, 2, MidpointRounding.AwayFromZero)
            .Should()
            .Be(18.02m);
        FargateSize.Default.Describe().Should().Be("0.5 vCPU and 1 GB");
    }

    [Test]
    public void Each_task_definition_carries_its_services_size()
    {
        var file = AwsStackTemplateTests.File_();
        file.Services["worker"].Memory = 4096;
        file.Services["worker"].Cpus = 1;

        var runtime = AwsStackTemplateTests.Templates(file).Runtime;
        var resources = (JsonObject)runtime["Resources"].NotNull();

        var worker = resources["WorkerTaskDefinition"].NotNull()["Properties"].NotNull();
        worker["Cpu"].NotNull().GetValue<string>().Should().Be("1024");
        worker["Memory"].NotNull().GetValue<string>().Should().Be("4096");
        var hub = resources["HubTaskDefinition"].NotNull()["Properties"].NotNull();
        hub["Cpu"].NotNull().GetValue<string>().Should().Be("512");
        hub["Memory"].NotNull().GetValue<string>().Should().Be("1024");
    }

    [Test]
    public void Every_trax_container_on_aws_drops_every_capability_and_has_a_read_only_root_and_a_scratch_tmp()
    {
        var runtime = AwsStackTemplateTests.Templates().Runtime;
        var definitions = ((JsonObject)runtime["Resources"].NotNull())
            .Where(r =>
                string.Equals(
                    r.Value.NotNull()["Type"].NotNull().GetValue<string>(),
                    "AWS::ECS::TaskDefinition",
                    StringComparison.Ordinal
                )
            )
            .ToList();

        definitions.Should().HaveCount(3, "hub, worker and migrate");
        foreach (var (id, definition) in definitions)
        {
            var properties = definition.NotNull()["Properties"].NotNull();
            var container = properties["ContainerDefinitions"]
                .NotNull()
                .AsArray()
                .Single()
                .NotNull();
            container["ReadonlyRootFilesystem"].NotNull().GetValue<bool>().Should().BeTrue(id);
            container["LinuxParameters"]
                .NotNull()["Capabilities"]
                .NotNull()["Drop"]
                .NotNull()
                .AsArray()
                .Select(c => c.NotNull().GetValue<string>())
                .Should()
                .Equal(["ALL"], id);
            container["MountPoints"]
                .NotNull()
                .AsArray()
                .Single()
                .NotNull()["ContainerPath"]
                .NotNull()
                .GetValue<string>()
                .Should()
                .Be("/tmp", id);
            properties["Volumes"]
                .NotNull()
                .AsArray()
                .Single()
                .NotNull()["Name"]
                .NotNull()
                .GetValue<string>()
                .Should()
                .Be("tmp", id);
        }
    }

    [Test]
    public void Plan_prices_each_service_at_its_own_size()
    {
        var file = AwsStackTemplateTests.File_();
        file.Services["worker"].Memory = 4096;

        var lines = AwsPlanSummary
            .Lines(file, "production", AwsStackTemplateTests.Templates(file))
            .ToList();

        lines
            .Should()
            .Contain(l =>
                l.Contains(
                    "hub: 1 Fargate task(s), 0.5 vCPU and 1 GB each",
                    StringComparison.Ordinal
                )
            );
        lines
            .Should()
            .Contain(l =>
                l.Contains(
                    "worker: 3 Fargate task(s), 0.5 vCPU and 4 GB each",
                    StringComparison.Ordinal
                )
            );
    }

    [Test]
    public void Pids_are_refused_on_aws()
    {
        var file = AwsStackTemplateTests.File_();
        file.Services["hub"].Pids = 1024;

        var act = () => file.Environment("production");

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*service 'hub' names pids, which aws (Fargate) cannot apply*");
    }

    [Test]
    public void Backing_limits_are_refused_on_aws()
    {
        var file = AwsStackTemplateTests.File_();
        file.Environments["production"].Limits["database"] = new DeployLimits { Memory = 2048 };

        var act = () => file.Environment("production");

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*limits are for compose and kubernetes*");
    }
}
