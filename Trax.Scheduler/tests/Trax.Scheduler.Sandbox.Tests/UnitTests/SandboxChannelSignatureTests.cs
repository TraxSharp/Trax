using System.Text;
using Trax.Scheduler.Services.RequestSigning;
using Trax.Scheduler.Services.Sandbox;

namespace Trax.Scheduler.Sandbox.Tests.UnitTests;

/// <summary>
/// The channel between hub and launcher: a request is authenticated by its MAC, and an answer
/// only as the answer to that request.
///
/// <para>Enforces <c>docs/adr/0020-a-sandbox-is-started-only-by-the-launcher-from-a-fixed-request.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0020-a-sandbox-is-started-only-by-the-launcher-from-a-fixed-request.md")]
[TestFixture]
public class SandboxChannelSignatureTests
{
    private const string Adr =
        "docs/adr/0020-a-sandbox-is-started-only-by-the-launcher-from-a-fixed-request.md";

    private static readonly byte[] Key = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
    private static readonly byte[] OtherKey = Enumerable
        .Range(1, 32)
        .Select(i => (byte)i)
        .ToArray();
    private static readonly byte[] Body = Encoding.UTF8.GetBytes("{\"runId\":\"x\"}");
    private const string Nonce = "00112233445566778899aabbccddeeff";

    [Test]
    public void TryVerify_ItsOwnSignature_Verifies()
    {
        var signature = SandboxChannelSignature.Sign(Key, Body, 1_700_000_000, Nonce);

        SandboxChannelSignature
            .TryVerify(Key, Body, signature, out var timestamp, out var nonce)
            .Should()
            .BeTrue();
        timestamp.Should().Be(1_700_000_000);
        nonce.Should().Be(Nonce);
    }

    [Test]
    public void TryVerify_ChangedBody_IsRefused()
    {
        var signature = SandboxChannelSignature.Sign(Key, Body, 1_700_000_000, Nonce);
        var changed = Encoding.UTF8.GetBytes("{\"runId\":\"y\"}");

        SandboxChannelSignature
            .TryVerify(Key, changed, signature, out _, out _)
            .Should()
            .BeFalse("the MAC covers the exact body");
    }

    [Test]
    public void TryVerify_OtherKey_IsRefused() =>
        SandboxChannelSignature
            .TryVerify(
                OtherKey,
                Body,
                SandboxChannelSignature.Sign(Key, Body, 1_700_000_000, Nonce),
                out _,
                out _
            )
            .Should()
            .BeFalse();

    [Test]
    public void TryVerify_ChangedTimestamp_IsRefused()
    {
        var signature = SandboxChannelSignature
            .Sign(Key, Body, 1_700_000_000, Nonce)
            .Replace("t=1700000000", "t=1700000001", StringComparison.Ordinal);

        SandboxChannelSignature
            .TryVerify(Key, Body, signature, out _, out _)
            .Should()
            .BeFalse("the timestamp is signed, so a stale request cannot be refreshed");
    }

    [Test]
    public void TryVerify_RunnerSignatureOverTheSameBody_IsRefused()
    {
        // A runner signature with the same key is a different protocol and must not start a sandbox.
        var runner = RunnerRequestSignature.Create(
            Key,
            RunnerRequestPurpose.Run,
            Body,
            TimeProvider.System
        );

        SandboxChannelSignature.TryVerify(Key, Body, runner, out _, out _).Should().BeFalse(Adr);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("v1")]
    [TestCase("v2,t=1,n=00112233445566778899aabbccddeeff,s=AAAA")]
    [TestCase("v1,t=-1,n=00112233445566778899aabbccddeeff,s=AAAA")]
    [TestCase("v1,t=1,n=short,s=AAAA")]
    [TestCase("v1,t=1,n=zz112233445566778899aabbccddeeff,s=AAAA")]
    [TestCase("v1,t=1,n=00112233445566778899aabbccddeeff,s=not base64!")]
    [TestCase("v1,t=1,n=00112233445566778899aabbccddeeff,s=AAAA")]
    public void TryVerify_Malformed_IsRefused(string? signature) =>
        SandboxChannelSignature.TryVerify(Key, Body, signature, out _, out _).Should().BeFalse();

    [Test]
    public void VerifyResponse_AnswerToThisRequest_Verifies()
    {
        var request = SandboxChannelSignature.Sign(Key, Body, 1_700_000_000, Nonce);
        var answer = Encoding.UTF8.GetBytes("{\"outcome\":\"Completed\"}");
        var response = SandboxChannelSignature.SignResponse(Key, request, answer);

        SandboxChannelSignature.VerifyResponse(Key, request, response, answer).Should().BeTrue();
    }

    [Test]
    public void VerifyResponse_AnswerToAnotherRequest_IsRefused()
    {
        var first = SandboxChannelSignature.Sign(Key, Body, 1_700_000_000, Nonce);
        var second = SandboxChannelSignature.Sign(
            Key,
            Body,
            1_700_000_000,
            "ffeeddccbbaa99887766554433221100"
        );
        var answer = Encoding.UTF8.GetBytes("{\"outcome\":\"Completed\"}");
        var response = SandboxChannelSignature.SignResponse(Key, first, answer);

        SandboxChannelSignature
            .VerifyResponse(Key, second, response, answer)
            .Should()
            .BeFalse("a captured answer cannot be replayed to another request");
    }

    [Test]
    public void VerifyResponse_RequestSignaturePresentedAsAnswer_IsRefused()
    {
        var request = SandboxChannelSignature.Sign(Key, Body, 1_700_000_000, Nonce);
        var mac = request[(request.LastIndexOf("s=", StringComparison.Ordinal) + 2)..];

        SandboxChannelSignature
            .VerifyResponse(Key, request, "v1,s=" + mac, Body)
            .Should()
            .BeFalse("a request MAC has its own prefix");
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("v1,s=***")]
    [TestCase("v2,s=AAAA")]
    public void VerifyResponse_Malformed_IsRefused(string? response) =>
        SandboxChannelSignature
            .VerifyResponse(Key, SandboxChannelSignature.Sign(Key, Body, 1, Nonce), response, Body)
            .Should()
            .BeFalse();

    [Test]
    public void Sign_ShortKey_Throws() =>
        FluentActions
            .Invoking(() => SandboxChannelSignature.Sign(new byte[31], Body, TimeProvider.System))
            .Should()
            .Throw<ArgumentException>();

    [Test]
    public void SignResponse_UnparseableRequestSignature_IsNull() =>
        SandboxChannelSignature.SignResponse(Key, "garbage", Body).Should().BeNull();
}
