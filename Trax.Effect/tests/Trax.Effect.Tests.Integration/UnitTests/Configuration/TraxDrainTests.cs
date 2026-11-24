using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Core.Extensions;
using Trax.Effect.Configuration.Deploy;
using Trax.Effect.Configuration.TraxEffectBuilder;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Extensions;

namespace Trax.Effect.Tests.Integration.UnitTests.Configuration;

/// <summary>
/// How a process is asked to drain (Trax.Docs/adr/0065): the arguments, the timeout, the force variable, and that a
/// drain or resume process never migrates and refuses to serve.
/// </summary>
[TestFixture]
public class TraxDrainTests
{
    [TestCase(new[] { "--trax-drain" }, true)]
    [TestCase(new[] { "--trax-drain", "00:05:00" }, true)]
    [TestCase(new[] { "--trax-drain=00:05:00" }, true)]
    [TestCase(new[] { "--urls", "http://+:8080", "--trax-resume" }, true)]
    [TestCase(new[] { "--trax-drainage" }, false)]
    [TestCase(new string[0], false)]
    public void The_arguments_are_found_anywhere_on_the_command_line(
        string[] args,
        bool expected
    ) => TraxDrain.RequestedIn(args).Should().Be(expected);

    [Test]
    public void No_drain_argument_means_no_timeout() =>
        TraxDrain.TimeoutIn(["--trax-resume"]).Should().BeNull();

    [Test]
    public void A_drain_without_a_value_waits_ten_minutes() =>
        TraxDrain.TimeoutIn(["--trax-drain", "--urls"]).Should().Be(TimeSpan.FromMinutes(10));

    [Test]
    public void The_timeout_follows_the_argument() =>
        TraxDrain.TimeoutIn(["--trax-drain", "00:05:00"]).Should().Be(TimeSpan.FromMinutes(5));

    [Test]
    public void The_timeout_can_be_joined_to_the_argument() =>
        TraxDrain.TimeoutIn(["--trax-drain=00:05:00"]).Should().Be(TimeSpan.FromMinutes(5));

    [TestCase("soon")]
    [TestCase("00:00:00")]
    [TestCase("-00:01:00")]
    [TestCase("1.00:00:01")]
    [TestCase("")]
    public void A_timeout_that_is_not_a_second_to_a_day_is_refused(string value)
    {
        var act = () => TraxDrain.TimeoutIn([$"--trax-drain={value}"]);

        act.Should().Throw<ArgumentException>().WithMessage("*00:10:00*");
    }

    [Test]
    public void Drain_and_resume_together_are_refused()
    {
        var act = () => TraxDrain.TimeoutIn(["--trax-drain", "--trax-resume"]);

        act.Should().Throw<ArgumentException>().WithMessage("*--trax-drain*--trax-resume*");
    }

    [TestCase(null, false)]
    [TestCase("", false)]
    [TestCase("false", false)]
    [TestCase(" TRUE ", true)]
    [TestCase("true", true)]
    public void The_force_variable_reads_true_or_false(string? value, bool expected) =>
        TraxDrain.ForceRequested(value).Should().Be(expected);

    [Test]
    public void Any_other_force_value_refuses_to_start()
    {
        var act = () => TraxDrain.ForceRequested("1");

        act.Should().Throw<InvalidOperationException>().WithMessage("*TRAX_DRAIN_FORCE*'1'*");
    }

    [Test]
    public void A_drain_process_does_not_migrate_even_when_nothing_says_skip() =>
        TraxMigrate
            .ShouldMigrate(Builder(), requested: false, variable: "", drains: true)
            .Should()
            .BeFalse("the deploy migrates before it drains, and a drain never changes the schema");

    [Test]
    public void Migrating_and_draining_in_one_process_is_refused()
    {
        var act = () =>
            TraxMigrate.ShouldMigrate(Builder(), requested: true, variable: "", drains: true);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*--trax-migrate*--trax-drain*");
    }

    [Test]
    public void A_drain_process_registers_the_gate_that_refuses_to_serve()
    {
        var services = new ServiceCollection();
        services.AddTrax(
            trax => trax.AddEffects(effects => effects.UseInMemory()),
            exportsDeploy: false,
            migrates: false,
            exportsClient: false,
            drains: true
        );

        services
            .Should()
            .ContainSingle(sd =>
                sd.ServiceType == typeof(IHostedService)
                && sd.ImplementationType == typeof(DrainServeGate)
            );
    }

    [Test]
    public async Task The_gate_refuses_a_drain_process_that_starts_to_serve()
    {
        var gate = new DrainServeGate();

        var act = async () => await gate.StartAsync(CancellationToken.None).ConfigureAwait(false);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*--trax-drain*RunTraxAsync*");
    }

    private static TraxEffectBuilder Builder()
    {
        TraxEffectBuilder? captured = null;
        new ServiceCollection().AddTrax(trax =>
            trax.AddEffects(effects =>
            {
                captured = effects;
                return captured;
            })
        );
        return captured.NotNull();
    }
}
