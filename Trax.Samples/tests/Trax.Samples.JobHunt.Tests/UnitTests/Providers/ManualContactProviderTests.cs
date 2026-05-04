using FluentAssertions;
using NUnit.Framework;
using Trax.Samples.JobHunt.Providers.Contact;

namespace Trax.Samples.JobHunt.Tests.UnitTests.Providers;

[TestFixture]
public class ManualContactProviderTests
{
    [Test]
    public async Task EnrichAsync_KnownNameProvided_PassesThroughUnverified()
    {
        var provider = new ManualContactProvider();

        var result = await provider.EnrichAsync("acme.example", knownName: "Alice");

        result.Name.Should().Be("Alice");
        result.Email.Should().BeNull();
        result.Verified.Should().BeFalse();
        result.Source.Should().Be("Manual");
    }

    [Test]
    public async Task EnrichAsync_NoKnownName_ReturnsNullName()
    {
        var provider = new ManualContactProvider();

        var result = await provider.EnrichAsync("acme.example", knownName: null);

        result.Name.Should().BeNull();
        result.Email.Should().BeNull();
        result.Verified.Should().BeFalse();
    }
}
