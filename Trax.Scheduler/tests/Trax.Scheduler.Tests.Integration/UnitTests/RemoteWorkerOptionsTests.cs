using FluentAssertions;
using Trax.Scheduler.Configuration;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

[TestFixture]
public class RemoteWorkerOptionsTests
{
    [Test]
    public void Defaults_TimeoutIs30Seconds()
    {
        var options = new RemoteWorkerOptions();
        options.Timeout.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Test]
    public void Defaults_BaseUrlIsNull()
    {
        var options = new RemoteWorkerOptions();
        options.BaseUrl.Should().BeNull();
    }

    [Test]
    public void Defaults_ConfigureHttpClientIsNull()
    {
        var options = new RemoteWorkerOptions();
        options.ConfigureHttpClient.Should().BeNull();
    }

    [Test]
    public void BaseUrl_CanBeSet()
    {
        var options = new RemoteWorkerOptions { BaseUrl = "https://example.com/trax/execute" };
        options.BaseUrl.Should().Be("https://example.com/trax/execute");
    }

    [Test]
    public void Timeout_CanBeCustomized()
    {
        var options = new RemoteWorkerOptions { Timeout = TimeSpan.FromMinutes(2) };
        options.Timeout.Should().Be(TimeSpan.FromMinutes(2));
    }

    [Test]
    public void ConfigureHttpClient_CanBeSet()
    {
        var called = false;
        var options = new RemoteWorkerOptions { ConfigureHttpClient = _ => called = true };

        options.ConfigureHttpClient.Should().NotBeNull();
        options.ConfigureHttpClient!(new HttpClient());
        called.Should().BeTrue();
    }
}
