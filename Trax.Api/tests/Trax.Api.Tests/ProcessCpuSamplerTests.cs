using FluentAssertions;
using NUnit.Framework;
using Trax.Api.Services.Metrics;

namespace Trax.Api.Tests;

[TestFixture]
public class ProcessCpuSamplerTests
{
    [Test]
    public void ComputePercent_HalfACoreBusy_OneCore_Is50()
    {
        ProcessCpuSampler.ComputePercent(TimeSpan.FromMilliseconds(500), 1000, 1).Should().Be(50.0);
    }

    [Test]
    public void ComputePercent_NormalisesByCoreCount()
    {
        // Same 500ms of CPU over 1s on 2 cores is only 25% of total capacity.
        ProcessCpuSampler
            .ComputePercent(TimeSpan.FromMilliseconds(500), 1000, 2)
            .Should()
            .Be(25.0);
    }

    [Test]
    public void ComputePercent_OverOneCore_ClampsTo100()
    {
        ProcessCpuSampler
            .ComputePercent(TimeSpan.FromMilliseconds(2000), 1000, 1)
            .Should()
            .Be(100.0);
    }

    [Test]
    public void ComputePercent_NonPositiveInterval_ReturnsNull()
    {
        ProcessCpuSampler.ComputePercent(TimeSpan.FromMilliseconds(100), 0, 1).Should().BeNull();
        ProcessCpuSampler.ComputePercent(TimeSpan.FromMilliseconds(100), -5, 1).Should().BeNull();
    }

    [Test]
    public void ComputePercent_ZeroCores_ReturnsNull()
    {
        ProcessCpuSampler.ComputePercent(TimeSpan.FromMilliseconds(100), 1000, 0).Should().BeNull();
    }

    [Test]
    public void SamplePercent_FirstCallPrimes_ThenComputesFromDelta()
    {
        // Two fixed samples 1s apart with 250ms of CPU consumed on one core.
        var samples = new Queue<(TimeSpan, DateTime)>(
            new[]
            {
                (TimeSpan.Zero, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
                (
                    TimeSpan.FromMilliseconds(250),
                    new DateTime(2026, 1, 1, 0, 0, 1, DateTimeKind.Utc)
                ),
            }
        );
        var sampler = new ProcessCpuSampler(() => samples.Dequeue(), cores: 1);

        sampler.SamplePercent().Should().BeNull(); // first call only primes the baseline
        sampler.SamplePercent().Should().Be(25.0); // 250ms / 1000ms / 1 core = 25%
    }

    [Test]
    public void SamplePercent_RealProcess_FirstCallIsNull()
    {
        // The parameterless ctor reads the live process; the first sample has no baseline.
        new ProcessCpuSampler()
            .SamplePercent()
            .Should()
            .BeNull();
    }
}
