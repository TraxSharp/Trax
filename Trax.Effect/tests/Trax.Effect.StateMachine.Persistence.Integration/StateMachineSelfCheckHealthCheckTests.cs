using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Trax.Effect.StateMachine.Persistence.Integration.Fakes;

namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary>
/// The self-check exposed as a health check: a deployed build proves it still reproduces every machine's
/// committed corpus, which is the drift a build-time test cannot see. Covers the check itself (healthy when
/// every machine agrees, unhealthy naming each machine that does not, corpus-less machines skipped rather
/// than failed) and the <see cref="StateMachineHealthCheckExtensions.AddTraxStateMachineSelfCheck"/>
/// registration end to end through <see cref="HealthCheckService"/>, since the whole point of the extension
/// is that a host adds it in one line and machines registered later are picked up with no edit.
/// </summary>
public class StateMachineSelfCheckHealthCheckTests
{
    // A corpus the declarative turnstile reproduces exactly: a transition (Locked + Coin(quarter) ->
    // Unlocked) and a rejection (Locked + Push has no transition), so replay exercises both outcomes.
    private const string PassCorpus = """
        {"cases":[{"given":{"machine":"health-pass","version":1,"state":"Locked","context":{}},"when":{"trigger":"Coin","input":{"coin":"quarter"}},"expect":{"outcome":"transitioned","wire":"{\"machine\":\"health-pass\",\"version\":1,\"state\":\"Unlocked\",\"context\":{\"paidWith\":\"quarter\"}}"}},{"given":{"machine":"health-pass","version":1,"state":"Locked","context":{}},"when":{"trigger":"Push"},"expect":{"outcome":"rejected","reason":"no-transition"}}]}
        """;

    // Same case under two other ids, but the golden expects wire the engine never produces.
    private const string FailCorpusA = """
        {"cases":[{"given":{"machine":"health-fail-a","version":1,"state":"Locked","context":{}},"when":{"trigger":"Coin","input":{"coin":"quarter"}},"expect":{"outcome":"transitioned","wire":"WRONG"}}]}
        """;

    private const string FailCorpusB = """
        {"cases":[{"given":{"machine":"health-fail-b","version":1,"state":"Locked","context":{}},"when":{"trigger":"Coin","input":{"coin":"quarter"}},"expect":{"outcome":"transitioned","wire":"WRONG"}}]}
        """;

    private sealed class HealthPassMachine : Machine<TurnstileState, TurnstileTrigger>
    {
        protected override void Configure(IMachineBuilder<TurnstileState, TurnstileTrigger> m) =>
            DeclarativeTurnstileMachine.ConfigureTurnstile(m, "health-pass");

        public override string? Corpus => PassCorpus;
    }

    private sealed class HealthFailMachineA : Machine<TurnstileState, TurnstileTrigger>
    {
        protected override void Configure(IMachineBuilder<TurnstileState, TurnstileTrigger> m) =>
            DeclarativeTurnstileMachine.ConfigureTurnstile(m, "health-fail-a");

        public override string? Corpus => FailCorpusA;
    }

    private sealed class HealthFailMachineB : Machine<TurnstileState, TurnstileTrigger>
    {
        protected override void Configure(IMachineBuilder<TurnstileState, TurnstileTrigger> m) =>
            DeclarativeTurnstileMachine.ConfigureTurnstile(m, "health-fail-b");

        public override string? Corpus => FailCorpusB;
    }

    private static Task<HealthCheckResult> Check(params IMachine[] machines) =>
        new StateMachineSelfCheckHealthCheck(machines).CheckHealthAsync(
            new HealthCheckContext
            {
                Registration = new HealthCheckRegistration(
                    "state-machines",
                    _ => new StateMachineSelfCheckHealthCheck(machines),
                    null,
                    null
                ),
            }
        );

    #region CheckHealthAsync

    [Test]
    public async Task CheckHealthAsync_is_healthy_when_every_machine_reproduces_its_corpus()
    {
        var result = await Check(new HealthPassMachine());

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("reproduces its committed corpus");
    }

    [Test]
    public async Task CheckHealthAsync_is_healthy_when_no_machines_are_registered()
    {
        var result = await Check();

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Test]
    public async Task CheckHealthAsync_skips_a_machine_that_ships_no_corpus_rather_than_failing_it()
    {
        var result = await Check(new DeclarativeTurnstileMachine());

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Test]
    public async Task CheckHealthAsync_is_unhealthy_and_names_the_machine_that_diverges()
    {
        var result = await Check(new HealthPassMachine(), new HealthFailMachineA());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("health-fail-a").And.NotContain("health-pass:");
    }

    [Test]
    public async Task CheckHealthAsync_reports_every_divergent_machine_not_just_the_first()
    {
        var result = await Check(new HealthFailMachineA(), new HealthFailMachineB());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("health-fail-a").And.Contain("health-fail-b");
    }

    #endregion

    #region AddTraxStateMachineSelfCheck

    private static ServiceProvider Host(Action<IServiceCollection> register, string? name = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var builder = services.AddHealthChecks();
        if (name is null)
            builder.AddTraxStateMachineSelfCheck();
        else
            builder.AddTraxStateMachineSelfCheck(name);
        register(services);
        return services.BuildServiceProvider();
    }

    [Test]
    public async Task AddTraxStateMachineSelfCheck_registers_a_check_named_state_machines()
    {
        await using var provider = Host(s => s.AddSingleton<IMachine, HealthPassMachine>());

        var report = await provider
            .GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(TestContext.CurrentContext.CancellationToken);

        report.Entries.Should().ContainKey("state-machines");
        report.Status.Should().Be(HealthStatus.Healthy);
    }

    [Test]
    public async Task AddTraxStateMachineSelfCheck_reports_unhealthy_when_a_registered_machine_diverges()
    {
        await using var provider = Host(s =>
        {
            s.AddSingleton<IMachine, HealthPassMachine>();
            s.AddSingleton<IMachine, HealthFailMachineA>();
        });

        var report = await provider
            .GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(TestContext.CurrentContext.CancellationToken);

        report.Status.Should().Be(HealthStatus.Unhealthy);
        report.Entries["state-machines"].Description.Should().Contain("health-fail-a");
    }

    [Test]
    public async Task AddTraxStateMachineSelfCheck_uses_a_caller_supplied_name()
    {
        await using var provider = Host(
            s => s.AddSingleton<IMachine, HealthPassMachine>(),
            "machines"
        );

        var report = await provider
            .GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(TestContext.CurrentContext.CancellationToken);

        report.Entries.Keys.Should().BeEquivalentTo(["machines"]);
    }

    [Test]
    public async Task AddTraxStateMachineSelfCheck_picks_up_a_machine_registered_after_it()
    {
        // The check resolves IEnumerable<IMachine> when it runs, not when it is added, which is what lets a
        // host add the line once and have a newly-discovered machine covered with no edit.
        await using var provider = Host(s => s.AddSingleton<IMachine, HealthFailMachineA>());

        var report = await provider
            .GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(TestContext.CurrentContext.CancellationToken);

        report.Status.Should().Be(HealthStatus.Unhealthy);
        report.Entries["state-machines"].Description.Should().Contain("health-fail-a");
    }

    [Test]
    public async Task AddTraxStateMachineSelfCheck_is_healthy_for_a_host_with_no_machines_at_all()
    {
        await using var provider = Host(_ => { });

        var report = await provider
            .GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(TestContext.CurrentContext.CancellationToken);

        report.Status.Should().Be(HealthStatus.Healthy);
    }

    #endregion
}
