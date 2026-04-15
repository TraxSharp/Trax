using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Trax.Api.GraphQL.Audit;

namespace Trax.Api.Tests.Audit;

[TestFixture]
public class TraxAuditWriterTests
{
    private static TraxAuditEntry SampleEntry(string id) =>
        new(id, "apikey", "op", "{ x }", null, 1, DateTimeOffset.UtcNow, true, null);

    private sealed class RecordingSink : ITraxAuditSink
    {
        public List<List<TraxAuditEntry>> Batches { get; } = [];

        public Task WriteAsync(IReadOnlyList<TraxAuditEntry> batch, CancellationToken ct)
        {
            Batches.Add([.. batch]);
            return Task.CompletedTask;
        }
    }

    private sealed class FailingSink(int failUntil) : ITraxAuditSink
    {
        public int Attempts { get; private set; }

        public Task WriteAsync(IReadOnlyList<TraxAuditEntry> batch, CancellationToken ct)
        {
            Attempts++;
            if (Attempts <= failUntil)
                throw new InvalidOperationException("sink down");
            return Task.CompletedTask;
        }
    }

    private static (TraxAuditChannel channel, TraxAuditWriter writer, ServiceProvider sp) Build(
        ITraxAuditSink sink,
        TraxAuditOptions opts
    )
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITraxAuditSink>(sink);
        services.AddSingleton(Options.Create(opts));
        var channel = new TraxAuditChannel(
            Options.Create(opts),
            NullLogger<TraxAuditChannel>.Instance
        );
        services.AddSingleton(channel);
        var sp = services.BuildServiceProvider();

        var writer = new TraxAuditWriter(
            channel,
            sp,
            Options.Create(opts),
            TimeProvider.System,
            NullLogger<TraxAuditWriter>.Instance
        );
        return (channel, writer, sp);
    }

    [Test]
    public async Task Drains_BatchFull_FlushesImmediately()
    {
        var sink = new RecordingSink();
        var (channel, writer, sp) = Build(
            sink,
            new TraxAuditOptions
            {
                BatchSize = 2,
                FlushInterval = TimeSpan.FromSeconds(30),
                ChannelCapacity = 100,
            }
        );
        using (sp)
        {
            channel.TryEnqueue(SampleEntry("a"));
            channel.TryEnqueue(SampleEntry("b"));
            channel.TryEnqueue(SampleEntry("c"));

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var writerTask = writer.StartAsync(cts.Token);
            await writerTask;
            await Task.Delay(300, cts.Token);

            sink.Batches.Should().ContainSingle();
            sink.Batches[0].Select(e => e.PrincipalId).Should().BeEquivalentTo(["a", "b"]);

            await writer.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task Drains_PartialBatch_FlushesOnInterval()
    {
        var sink = new RecordingSink();
        var (channel, writer, sp) = Build(
            sink,
            new TraxAuditOptions
            {
                BatchSize = 50,
                FlushInterval = TimeSpan.FromMilliseconds(100),
                ChannelCapacity = 100,
            }
        );
        using (sp)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await writer.StartAsync(cts.Token);

            channel.TryEnqueue(SampleEntry("a"));
            channel.TryEnqueue(SampleEntry("b"));

            await Task.Delay(500, cts.Token);
            sink.Batches.Should().NotBeEmpty();
            sink.Batches.SelectMany(b => b).Select(e => e.PrincipalId).Should().Contain(["a", "b"]);

            await writer.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task SinkThrows_Retries_ThenSucceeds()
    {
        var sink = new FailingSink(failUntil: 2);
        var (channel, writer, sp) = Build(
            sink,
            new TraxAuditOptions
            {
                BatchSize = 1,
                FlushInterval = TimeSpan.FromMilliseconds(100),
                MaxRetries = 3,
                RetryBackoff = TimeSpan.FromMilliseconds(10),
                ChannelCapacity = 100,
            }
        );
        using (sp)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await writer.StartAsync(cts.Token);
            channel.TryEnqueue(SampleEntry("a"));

            await Task.Delay(800, cts.Token);

            sink.Attempts.Should().BeGreaterThanOrEqualTo(3);

            await writer.StopAsync(CancellationToken.None);
        }
    }
}
