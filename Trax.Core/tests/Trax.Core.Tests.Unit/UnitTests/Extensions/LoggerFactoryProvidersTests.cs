using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Trax.Core.Extensions;

namespace Trax.Core.Tests.Unit.UnitTests.Extensions;

public class LoggerFactoryProvidersTests : TestSetup
{
    [Test]
    public void GetLoggerProviders_FactoryWithNoProviders_ReturnsEmpty()
    {
        var factory = NullLoggerFactory.Instance;

        var providers = factory.GetLoggerProviders();

        providers.Should().NotBeNull();
        providers.Should().BeEmpty();
    }

    [Test]
    public void GetLoggerProviders_RealFactoryWithProvider_DoesNotThrow()
    {
        var factory = LoggerFactory.Create(b => b.AddProvider(NullLoggerProvider.Instance));

        var providers = factory.GetLoggerProviders();

        // The reflection-based reader may return [] for built-in factory shapes that
        // changed between framework versions; what we care about is no exception.
        providers.Should().NotBeNull();
    }
}
