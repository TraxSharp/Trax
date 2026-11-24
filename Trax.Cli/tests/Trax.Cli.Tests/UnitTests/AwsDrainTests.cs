using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.Cli.Deploy.Aws;
using Trax.Core.Extensions;
using Trax.Effect.Configuration.Deploy;

namespace Trax.Cli.Tests.UnitTests;

/// <summary>
/// An ECS deploy over a running stack drains the hosts after the migration and before the services roll (a task on
/// the migrate task definition with the drain's command), and ends the drain after the roll. ECS stops a container
/// within 120 seconds at most, so the drain finishes before the roll rather than in the stop
/// (<c>Trax.Cli/docs/adr/0007</c>).
/// </summary>
[Property("adr", "docs/adr/0007-hubs-roll-with-overlap-after-a-drain.md")]
public class AwsDrainTests
{
    private const string Adr =
        "see cli/0007 (docs/adr/0007-hubs-roll-with-overlap-after-a-drain.md)";
    private const string Tag = "20261124-120000-abc";

    private static (AwsDeployer, AwsDeployerTests.FakeAws, List<string>) Deployer(bool running)
    {
        var aws = new AwsDeployerTests.FakeAws();
        aws.Secrets.Add("orders-production/app/Stripe__ApiKey");
        if (running)
            aws.Existing = AwsDeployerTests.FakeAws.Running();
        var said = new List<string>();
        return (new AwsDeployer(aws, new AwsDeployerTests.FakeImages(), said.Add), aws, said);
    }

    private static Task<string?> Deploy(AwsDeployer deployer, bool force = false) =>
        deployer.DeployAsync(
            AwsStackTemplateTests.File_(),
            "/src/trax.deploy.json",
            "production",
            AwsStackTemplateTests.Exports(),
            Tag,
            CancellationToken.None,
            force
        );

    [Test]
    public async Task A_running_stack_is_drained_after_the_migration_and_resumed_after_the_roll()
    {
        var (deployer, aws, _) = Deployer(running: true);

        await Deploy(deployer);

        aws.Calls.Should()
            .Equal(
                [
                    "deploy-base",
                    "deploy",
                    "run-task",
                    "run-task drain",
                    "deploy",
                    "run-task resume",
                ],
                Adr
            );
        var drain = aws.Tasks[1];
        drain
            .TaskDefinition.Should()
            .Be("migrate-td", "the drain runs the new image, as the migration does");
        drain.Container.Should().Be(AwsStack.MigrateContainer);
        drain.Command.Should().Equal(TraxDrain.Argument, "00:10:00");
        drain.Environment.NotNull()[TraxMigrate.Variable].Should().Be(TraxMigrate.SkipValue);
        drain.Environment.Should().NotContainKey(TraxDrain.ForceVariable);
        aws.Tasks[2].Command.Should().Equal(TraxDrain.ResumeArgument);
        aws.Tasks[0]
            .Command.Should()
            .BeNull("the migration runs the task definition's own command");
    }

    [Test]
    public async Task A_new_stack_has_nothing_to_drain()
    {
        var (deployer, aws, _) = Deployer(running: false);

        await Deploy(deployer);

        aws.Calls.Should().Equal("deploy-base", "deploy", "run-task", "deploy");
    }

    [Test]
    public async Task Force_reaches_the_drain_task()
    {
        var (deployer, aws, _) = Deployer(running: true);

        await Deploy(deployer, force: true);

        aws.Tasks[1].Environment.NotNull()[TraxDrain.ForceVariable].Should().Be("true");
    }

    [Test]
    public async Task A_drain_with_work_still_busy_refuses_with_its_log_and_rolls_nothing()
    {
        var (deployer, aws, _) = Deployer(running: true);
        aws.ExitCodes["drain"] = 134;
        aws.LogLines.Add(
            "Still busy after the drain's timeout of 00:10:00, so the drain was ended"
        );
        aws.LogLines.Add(
            "  run 42 Orders.IBillTrain (InProgress, since 2026-11-24 12:00:00Z, on task-1)"
        );

        var act = () => Deploy(deployer);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage(
            "The drain ended with exit code 134, so no service was moved*run 42 Orders.IBillTrain*"
                + "*trax deploy --env production --force*"
        );
        aws.Calls.Should().Equal("deploy-base", "deploy", "run-task", "run-task drain");
    }

    [Test]
    public async Task A_roll_that_fails_after_the_drain_still_ends_the_drain()
    {
        var (deployer, aws, _) = Deployer(running: true);
        aws.FailDeployNumber = 3;

        var act = () => Deploy(deployer);

        await act.Should().ThrowAsync<InvalidOperationException>();
        aws.Calls[^1].Should().Be("run-task resume");
    }

    [Test]
    public async Task A_resume_that_fails_leaves_the_deploy_done_and_says_the_drain_lapses()
    {
        var (deployer, aws, said) = Deployer(running: true);
        aws.ExitCodes["resume"] = 1;

        await Deploy(deployer);

        said.Should().Contain(s => s.Contains("lapses on its own", StringComparison.Ordinal));
    }

    [Test]
    public void Every_task_container_stops_within_fargates_most()
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
            .Select(r =>
                r.Value.NotNull()["Properties"]
                    .NotNull()["ContainerDefinitions"]
                    .NotNull()[0]
                    .NotNull()
            )
            .ToList();

        definitions.Should().NotBeEmpty();
        foreach (var container in definitions)
            container["StopTimeout"].NotNull().GetValue<int>().Should().Be(120, Adr);
    }
}
