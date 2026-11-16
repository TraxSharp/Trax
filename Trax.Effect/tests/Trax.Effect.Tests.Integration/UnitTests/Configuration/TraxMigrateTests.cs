using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Effect.Configuration.Deploy;
using Trax.Effect.Configuration.TraxEffectBuilder;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;

namespace Trax.Effect.Tests.Integration.UnitTests.Configuration;

/// <summary>
/// Whether a data provider migrates while it registers: by default yes, not with <c>SkipMigrations()</c> or
/// <c>TRAX_MIGRATIONS=skip</c>, and always for a process started with <c>--trax-migrate</c>, unless its code says it
/// never migrates.
/// </summary>
[TestFixture]
public class TraxMigrateTests
{
    [Test]
    public void A_host_migrates_by_default() =>
        TraxMigrate.ShouldMigrate(Builder(), requested: false, variable: "").Should().BeTrue();

    [TestCase("skip")]
    [TestCase(" SKIP ")]
    public void The_variable_set_to_skip_stops_the_migration(string value) =>
        TraxMigrate.ShouldMigrate(Builder(), requested: false, variable: value).Should().BeFalse();

    [Test]
    public void Any_other_value_of_the_variable_refuses_to_start()
    {
        var act = () => TraxMigrate.ShouldMigrate(Builder(), requested: false, variable: "off");

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*TRAX_MIGRATIONS*'off'*'skip'*");
    }

    [Test]
    public void SkipMigrations_in_code_stops_the_migration() =>
        TraxMigrate
            .ShouldMigrate(Builder(skipInCode: true), requested: false, variable: "")
            .Should()
            .BeFalse();

    [Test]
    public void A_migrate_process_migrates_even_with_the_variable_set_and_refuses_to_serve()
    {
        var services = new ServiceCollection();

        TraxMigrate
            .ShouldMigrate(Builder(services: services), requested: true, variable: "skip")
            .Should()
            .BeTrue();

        services
            .Should()
            .Contain(sd =>
                sd.ServiceType == typeof(IHostedService)
                && sd.ImplementationType == typeof(MigrateServeGate)
            );
    }

    [Test]
    public void A_migrate_process_whose_code_never_migrates_refuses_to_start()
    {
        var act = () =>
            TraxMigrate.ShouldMigrate(Builder(skipInCode: true), requested: true, variable: "");

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*--trax-migrate*SkipMigrations()*");
    }

    [Test]
    public async Task The_gate_refuses_a_migrate_process_that_starts_to_serve()
    {
        var gate = new MigrateServeGate();

        var act = async () => await gate.StartAsync(CancellationToken.None);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*--trax-migrate*RunTraxAsync*");
    }

    [TestCase(new[] { "--trax-migrate" }, true)]
    [TestCase(new[] { "--urls", "http://+:8080", "--trax-migrate" }, true)]
    [TestCase(new[] { "--trax-migrate=yes" }, false)]
    [TestCase(new string[0], false)]
    public void The_argument_is_found_anywhere_on_the_command_line(string[] args, bool expected) =>
        TraxMigrate.RequestedIn(args).Should().Be(expected);

    private static TraxEffectBuilder Builder(
        bool skipInCode = false,
        IServiceCollection? services = null
    )
    {
        TraxEffectBuilder? captured = null;
        (services ?? new ServiceCollection()).AddTrax(trax =>
            trax.AddEffects(effects =>
            {
                captured = skipInCode ? effects.SkipMigrations() : effects;
                return captured;
            })
        );
        return captured!;
    }
}
