using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Trax.Samples.JobHunt.Providers.Email;

namespace Trax.Samples.JobHunt.Tests.UnitTests.Providers;

[TestFixture]
public class StubEmailSenderTests
{
    [Test]
    public async Task SendAsync_ReturnsStubMessageIdAndSourceTag()
    {
        var sender = new StubEmailSender(NullLogger<StubEmailSender>.Instance);

        var result = await sender.SendAsync(
            "to@example.com",
            "subject",
            "body",
            attachmentPath: null
        );

        result.MessageId.Should().StartWith("stub-");
        result.MessageId.Should().HaveLength("stub-".Length + 32);
        result.Provider.Should().Be("Stub");
    }

    [Test]
    public async Task SendAsync_GeneratesUniqueIdsAcrossCalls()
    {
        var sender = new StubEmailSender(NullLogger<StubEmailSender>.Instance);

        var a = await sender.SendAsync("a@x", "s", "b", null);
        var b = await sender.SendAsync("a@x", "s", "b", null);

        a.MessageId.Should().NotBe(b.MessageId);
    }

    [Test]
    public async Task SendAsync_AttachmentPathIgnored_StillSucceeds()
    {
        var sender = new StubEmailSender(NullLogger<StubEmailSender>.Instance);

        var result = await sender.SendAsync("to@x", "s", "b", attachmentPath: "/tmp/attached.pdf");

        result.MessageId.Should().StartWith("stub-");
    }
}
