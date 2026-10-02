using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Decisions;
using Trax.Effect.Decisions.SystemOne;
using Trax.Effect.Decisions.SystemOne.Extensions;
using Trax.Effect.Extensions;

namespace Trax.Effect.Tests.Integration.UnitTests.Decisions;

/// <summary>
/// <c>AddNimbleDecider</c> reaches Nimble on a server the caller runs. These pin the defaults taken
/// from Nimble's own serving code, that there is no default endpoint, and how several System One
/// deciders are registered side by side.
/// </summary>
public class NimbleDeciderTests
{
    private static readonly Uri Server = new("http://localhost:8000/v1/systemone");

    [Test]
    public void ByDefault_ItAsksForNimblesCheckpointWithinItsServersLimits()
    {
        var options = new NimbleOptions { Endpoint = Server }.ToSystemOne();

        options.Endpoint.Should().Be(Server);
        options.Model.Should().Be("bespokelabs/Bespoke-Nimble-9B");
        options.ApiKey.Should().BeNull();
        options
            .MaxConcurrentRequests.Should()
            .Be(4, "Nimble's server runs four evaluations per container and answers 529 beyond");
        options.MaxQuestions.Should().Be(64);
        options.MaxOptions.Should().Be(26);
        options.AttemptTimeout.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Test]
    public void AnythingSetExplicitly_OverridesTheDefaults()
    {
        var options = new NimbleOptions
        {
            Endpoint = new Uri("https://nimble.internal.example/v1/systemone"),
            ApiKey = "nimble-key",
            Model = "bespokelabs/Bespoke-Nimble-9B-v2",
            MaxConcurrentRequests = 12,
            MaxOptions = 255,
        }.ToSystemOne();

        options.Endpoint!.Host.Should().Be("nimble.internal.example");
        options.ApiKey.Should().Be("nimble-key");
        options.Model.Should().Be("bespokelabs/Bespoke-Nimble-9B-v2");
        options.MaxConcurrentRequests.Should().Be(12);
        options.MaxOptions.Should().Be(255);
    }

    [Test]
    public void WithoutAnEndpoint_StopsTheHostFromStarting()
    {
        var register = () =>
            new ServiceCollection().AddTrax(trax =>
                trax.AddEffects(effects => effects.AddNimbleDecider(_ => { }))
            );

        register
            .Should()
            .Throw<ArgumentException>()
            .WithMessage("*Endpoint is required*Nimble server you run*");
    }

    [Test]
    public void AddNimbleDecider_RegistersTheDeciderTrainsFind()
    {
        using var provider = new ServiceCollection()
            .AddTrax(trax =>
                trax.AddEffects(effects => effects.AddNimbleDecider(o => o.Endpoint = Server))
            )
            .BuildServiceProvider();

        provider.GetRequiredService<IDecider>().Should().BeOfType<SystemOneDecider>();
        provider
            .GetRequiredService<IDecider>()
            .Should()
            .BeSameAs(provider.GetRequiredService<SystemOneDecider>());
    }

    [TestCase("nimble-latest")]
    [TestCase("nimble:latest")]
    [TestCase("nimble")]
    public void AnUnpinnedModel_StopsTheHostFromStarting(string model)
    {
        var register = () =>
            new ServiceCollection().AddTrax(trax =>
                trax.AddEffects(effects =>
                    effects.AddNimbleDecider(o =>
                    {
                        o.Endpoint = Server;
                        o.Model = model;
                    })
                )
            );

        register.Should().Throw<ArgumentException>().WithMessage("*floating alias*");
    }

    [Test]
    public void PlainHttpToAnotherMachine_StopsTheHostFromStarting()
    {
        var register = () =>
            new ServiceCollection().AddTrax(trax =>
                trax.AddEffects(effects =>
                    effects.AddNimbleDecider(o =>
                        o.Endpoint = new Uri("http://gpu-box.internal:8000/v1/systemone")
                    )
                )
            );

        register.Should().Throw<ArgumentException>().WithMessage("*is not HTTPS*");
    }

    [Test]
    public void NamedDeciders_RegisterSideBySideForACascade()
    {
        var services = new ServiceCollection().AddTrax(trax =>
            trax.AddEffects(effects =>
                effects
                    .AddNimbleDecider("nimble", o => o.Endpoint = Server)
                    .AddSystemOneDecider(
                        "jev",
                        o =>
                        {
                            o.Endpoint = new Uri("https://api.example.test/v1/systemone");
                            o.Model = "jev-1.13.0";
                            o.ApiKey = "sk-test";
                        }
                    )
            )
        );
        services.AddSingleton<IDecider>(sp => new CascadingDecider(
            sp.GetRequiredKeyedService<SystemOneDecider>("nimble"),
            sp.GetRequiredKeyedService<SystemOneDecider>("jev")
        ));
        using var provider = services.BuildServiceProvider();

        var nimble = provider.GetRequiredKeyedService<SystemOneDecider>("nimble");
        var jev = provider.GetRequiredKeyedService<SystemOneDecider>("jev");

        nimble.Should().NotBeSameAs(jev);
        nimble.Options.Model.Should().Be("bespokelabs/Bespoke-Nimble-9B");
        nimble.Options.Endpoint.Should().Be(Server);
        jev.Options.Model.Should().Be("jev-1.13.0");
        jev.Options.ApiKey.Should().Be("sk-test");
        provider.GetRequiredService<IDecider>().Should().BeOfType<CascadingDecider>();
        provider
            .GetService<SystemOneDecider>()
            .Should()
            .BeNull("a named decider is not the one every train asks");
    }

    [Test]
    public void TwoUnnamedDeciders_StopTheHostFromStartingRatherThanTheLastWinning()
    {
        var register = () =>
            new ServiceCollection().AddTrax(trax =>
                trax.AddEffects(effects =>
                    effects
                        .AddNimbleDecider(o => o.Endpoint = Server)
                        .AddSystemOneDecider(o =>
                        {
                            o.Endpoint = new Uri("https://api.example.test/v1/systemone");
                            o.Model = "jev-1.13.0";
                        })
                )
            );

        register
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*already registered as the IDecider*give each a name*");
    }

    [Test]
    public void TheSameNameTwice_StopsTheHostFromStarting()
    {
        var register = () =>
            new ServiceCollection().AddTrax(trax =>
                trax.AddEffects(effects =>
                    effects
                        .AddNimbleDecider("tier1", o => o.Endpoint = Server)
                        .AddNimbleDecider("tier1", o => o.Endpoint = Server)
                )
            );

        register
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*named 'tier1' is already registered*");
    }

    [Test]
    public async Task TheContainer_DisposesTheDeciderItBuilt()
    {
        var provider = new ServiceCollection()
            .AddTrax(trax =>
                trax.AddEffects(effects => effects.AddNimbleDecider(o => o.Endpoint = Server))
            )
            .BuildServiceProvider();
        var decider = provider.GetRequiredService<SystemOneDecider>();

        await provider.DisposeAsync();

        var decide = () =>
            decider.Decide(
                new DecisionRequest("T", "state", [new YesNoQuestion("Q", "?", null, null)]),
                CancellationToken.None
            );
        await decide.Should().ThrowAsync<ObjectDisposedException>();
    }
}
