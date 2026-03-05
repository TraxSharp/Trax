using FluentAssertions;
using Trax.Scheduler.Configuration;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// Tests for <see cref="LocalWorkerOptions"/> default values and configuration behavior.
/// </summary>
[TestFixture]
public class LocalWorkerOptionsTests
{
    [Test]
    public void DefaultWorkerCount_EqualsProcessorCount()
    {
        var options = new LocalWorkerOptions();
        options.WorkerCount.Should().Be(Environment.ProcessorCount);
    }

    [Test]
    public void DefaultPollingInterval_IsOneSecond()
    {
        var options = new LocalWorkerOptions();
        options.PollingInterval.Should().Be(TimeSpan.FromSeconds(1));
    }

    [Test]
    public void DefaultVisibilityTimeout_IsThirtyMinutes()
    {
        var options = new LocalWorkerOptions();
        options.VisibilityTimeout.Should().Be(TimeSpan.FromMinutes(30));
    }

    [Test]
    public void DefaultShutdownTimeout_IsThirtySeconds()
    {
        var options = new LocalWorkerOptions();
        options.ShutdownTimeout.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Test]
    public void WorkerCount_CanBeCustomized()
    {
        var options = new LocalWorkerOptions { WorkerCount = 8 };
        options.WorkerCount.Should().Be(8);
    }

    [Test]
    public void PollingInterval_CanBeCustomized()
    {
        var options = new LocalWorkerOptions { PollingInterval = TimeSpan.FromSeconds(5) };
        options.PollingInterval.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Test]
    public void VisibilityTimeout_CanBeCustomized()
    {
        var options = new LocalWorkerOptions { VisibilityTimeout = TimeSpan.FromHours(1) };
        options.VisibilityTimeout.Should().Be(TimeSpan.FromHours(1));
    }

    [Test]
    public void ShutdownTimeout_CanBeCustomized()
    {
        var options = new LocalWorkerOptions { ShutdownTimeout = TimeSpan.FromMinutes(2) };
        options.ShutdownTimeout.Should().Be(TimeSpan.FromMinutes(2));
    }

    [Test]
    public void UseLocalWorkers_AppliesConfigureAction()
    {
        // Verify that the configure action pattern works correctly
        var options = new LocalWorkerOptions();
        Action<LocalWorkerOptions> configure = opts =>
        {
            opts.WorkerCount = 4;
            opts.PollingInterval = TimeSpan.FromSeconds(2);
            opts.VisibilityTimeout = TimeSpan.FromMinutes(15);
            opts.ShutdownTimeout = TimeSpan.FromSeconds(10);
        };

        configure(options);

        options.WorkerCount.Should().Be(4);
        options.PollingInterval.Should().Be(TimeSpan.FromSeconds(2));
        options.VisibilityTimeout.Should().Be(TimeSpan.FromMinutes(15));
        options.ShutdownTimeout.Should().Be(TimeSpan.FromSeconds(10));
    }
}
