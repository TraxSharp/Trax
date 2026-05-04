using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Extensions;
using Trax.Mediator.Configuration;
using Trax.Mediator.Extensions;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.UnitTests;

[TestFixture]
public class TraxMediatorBuilderSettingsTests
{
    [Test]
    public void AllowMissingAuthorizationService_FlipsFlagOnConfiguration()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects)
                .AddMediator(m =>
                    m.ScanAssemblies(typeof(AssemblyMarker).Assembly)
                        .AllowMissingAuthorizationService()
                )
        );

        using var provider = services.BuildServiceProvider();
        var config = provider.GetRequiredService<MediatorConfiguration>();

        config.AllowMissingAuthorizationService.Should().BeTrue();
    }

    [Test]
    public void AllowMissingAuthorizationService_DefaultIsFalse()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects)
                .AddMediator(m => m.ScanAssemblies(typeof(AssemblyMarker).Assembly))
        );

        using var provider = services.BuildServiceProvider();
        var config = provider.GetRequiredService<MediatorConfiguration>();

        config.AllowMissingAuthorizationService.Should().BeFalse();
    }

    [Test]
    public void WithMaxInputJsonBytes_PositiveValue_AppliesToConfiguration()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects)
                .AddMediator(m =>
                    m.ScanAssemblies(typeof(AssemblyMarker).Assembly).WithMaxInputJsonBytes(2048)
                )
        );

        using var provider = services.BuildServiceProvider();
        var config = provider.GetRequiredService<MediatorConfiguration>();

        config.MaxInputJsonBytes.Should().Be(2048);
    }

    [Test]
    public void WithMaxInputJsonBytes_ZeroOrNegative_Throws()
    {
        var builder = new TraxMediatorBuilder(null!);

        Action zero = () => builder.WithMaxInputJsonBytes(0);
        Action negative = () => builder.WithMaxInputJsonBytes(-1);

        zero.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*positive*");
        negative.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*positive*");
    }

    [Test]
    public void PerPrincipalMaxConcurrentRun_PositiveValue_AppliesToConfiguration()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects)
                .AddMediator(m =>
                    m.ScanAssemblies(typeof(AssemblyMarker).Assembly)
                        .PerPrincipalMaxConcurrentRun(7)
                )
        );

        using var provider = services.BuildServiceProvider();
        var config = provider.GetRequiredService<MediatorConfiguration>();

        config.PerPrincipalMaxConcurrentRun.Should().Be(7);
    }

    [Test]
    public void PerPrincipalMaxConcurrentRun_ZeroOrNegative_Throws()
    {
        var builder = new TraxMediatorBuilder(null!);

        Action zero = () => builder.PerPrincipalMaxConcurrentRun(0);
        Action negative = () => builder.PerPrincipalMaxConcurrentRun(-3);

        zero.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*positive*");
        negative.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*positive*");
    }
}
