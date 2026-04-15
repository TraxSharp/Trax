using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Trax.Api.GraphQL.Audit;

namespace Trax.Api.Tests.Audit;

[TestFixture]
public class TraxAuditChannelTests
{
    private static TraxAuditChannel NewChannel(int capacity) =>
        new(
            Options.Create(new TraxAuditOptions { ChannelCapacity = capacity }),
            NullLogger<TraxAuditChannel>.Instance
        );

    private static TraxAuditEntry SampleEntry(string principalId = "u") =>
        new(
            PrincipalId: principalId,
            PrincipalType: "apikey",
            OperationName: "op",
            Document: "{ x }",
            Variables: null,
            DurationMs: 1,
            Timestamp: DateTimeOffset.UtcNow,
            Success: true,
            ErrorText: null
        );

    [Test]
    public void Enqueue_BelowCapacity_Succeeds()
    {
        using var channel = NewChannel(5);

        channel.TryEnqueue(SampleEntry()).Should().BeTrue();
    }

    [Test]
    public void Enqueue_AtCapacity_DropsWriteAndIncrementsCounter()
    {
        using var channel = NewChannel(2);
        channel.TryEnqueue(SampleEntry("a"));
        channel.TryEnqueue(SampleEntry("b"));

        var accepted = channel.TryEnqueue(SampleEntry("c"));

        accepted.Should().BeFalse();
        channel.TotalDropped.Should().Be(1);
    }

    [Test]
    public async Task Reader_ReadsInOrder()
    {
        using var channel = NewChannel(10);
        channel.TryEnqueue(SampleEntry("a"));
        channel.TryEnqueue(SampleEntry("b"));
        channel.Complete();

        var first = await channel.Reader.ReadAsync();
        var second = await channel.Reader.ReadAsync();

        first.PrincipalId.Should().Be("a");
        second.PrincipalId.Should().Be("b");
    }
}
