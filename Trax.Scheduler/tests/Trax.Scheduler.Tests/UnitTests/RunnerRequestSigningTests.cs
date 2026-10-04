using System.Net;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.Http;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Services.RequestSigning;
using Trax.Scheduler.Services.RunExecutor;

namespace Trax.Scheduler.Tests.UnitTests;

/// <summary>
/// The runner request signature and the verifier's posture, freshness and replay checks, plus the
/// HTTP senders that attach the signature. Enforces <c>docs/adr/0006-a-runner-requires-an-authorization-posture.md</c>.
/// </summary>
[Property("adr", "docs/adr/0006-a-runner-requires-an-authorization-posture.md")]
[TestFixture]
public class RunnerRequestSigningTests
{
    private static readonly byte[] Key = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
    private static readonly byte[] Body = Encoding.UTF8.GetBytes("""{"metadataId":1}""");

    private static RunnerRequestVerifier Verifier(
        Action<TraxJobRunnerOptions>? configure = null,
        TimeProvider? time = null
    )
    {
        var options = new TraxJobRunnerOptions { SigningKey = Key };
        configure?.Invoke(options);
        return new RunnerRequestVerifier(
            options,
            NullLogger<RunnerRequestVerifier>.Instance,
            new InMemoryNonceStore(),
            time
        );
    }

    private static string Sign(
        RunnerRequestPurpose purpose = RunnerRequestPurpose.Execute,
        byte[]? body = null
    ) => RunnerRequestSignature.Create(Key, purpose, body ?? Body);

    #region Verification

    [Test]
    public async Task Verify_ValidSignature_IsAccepted() =>
        (
            await Verifier()
                .VerifyAsync(RunnerRequestPurpose.Execute, Body, Sign(), requireFresh: true)
        )
            .Should()
            .Be(RunnerRequestVerdict.Accepted);

    [Test]
    public async Task Verify_NoSignature_IsMissing() =>
        (await Verifier().VerifyAsync(RunnerRequestPurpose.Execute, Body, null, requireFresh: true))
            .Should()
            .Be(RunnerRequestVerdict.Missing);

    [Test]
    public async Task Verify_BodyChangedAfterSigning_IsInvalid() =>
        (
            await Verifier()
                .VerifyAsync(
                    RunnerRequestPurpose.Execute,
                    Encoding.UTF8.GetBytes("""{"metadataId":2}"""),
                    Sign(),
                    requireFresh: true
                )
        )
            .Should()
            .Be(RunnerRequestVerdict.Invalid);

    [Test]
    public async Task Verify_SignedForTheOtherPurpose_IsInvalid() =>
        (
            await Verifier()
                .VerifyAsync(
                    RunnerRequestPurpose.Run,
                    Body,
                    Sign(RunnerRequestPurpose.Execute),
                    requireFresh: true
                )
        )
            .Should()
            .Be(RunnerRequestVerdict.Invalid);

    [TestCase("")]
    [TestCase("garbage")]
    [TestCase("v2,t=1,n=00000000000000000000000000000000,s=AAAA")]
    [TestCase("v1,t=abc,n=00000000000000000000000000000000,s=AAAA")]
    [TestCase("v1,t=1,n=short,s=AAAA")]
    [TestCase("v1,t=1,n=00000000000000000000000000000000,s=not base64!")]
    public async Task Verify_MalformedSignature_IsRefused(string signature) =>
        (
            await Verifier()
                .VerifyAsync(RunnerRequestPurpose.Execute, Body, signature, requireFresh: true)
        )
            .Should()
            .BeOneOf(RunnerRequestVerdict.Missing, RunnerRequestVerdict.Invalid);

    [Test]
    public async Task Verify_TimestampOutsideTheSkew_IsStale()
    {
        var old = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds();
        var signature = RunnerRequestSignature.Create(
            Key,
            RunnerRequestPurpose.Run,
            Body,
            old,
            Convert.ToHexString(Guid.NewGuid().ToByteArray())
        );

        (
            await Verifier()
                .VerifyAsync(RunnerRequestPurpose.Run, Body, signature, requireFresh: true)
        )
            .Should()
            .Be(RunnerRequestVerdict.Stale);
    }

    [Test]
    public async Task Verify_TimestampOutsideTheSkew_WhenFreshnessIsNotRequired_IsAccepted()
    {
        var old = DateTimeOffset.UtcNow.AddHours(-3).ToUnixTimeSeconds();
        var signature = RunnerRequestSignature.Create(
            Key,
            RunnerRequestPurpose.Execute,
            Body,
            old,
            Convert.ToHexString(Guid.NewGuid().ToByteArray())
        );

        (
            await Verifier()
                .VerifyAsync(RunnerRequestPurpose.Execute, Body, signature, requireFresh: false)
        )
            .Should()
            .Be(RunnerRequestVerdict.Accepted);
    }

    [Test]
    public async Task Verify_SameSignatureTwice_IsReplayed()
    {
        var verifier = Verifier();
        var signature = Sign(RunnerRequestPurpose.Run);

        (await verifier.VerifyAsync(RunnerRequestPurpose.Run, Body, signature, requireFresh: true))
            .Should()
            .Be(RunnerRequestVerdict.Accepted);
        (await verifier.VerifyAsync(RunnerRequestPurpose.Run, Body, signature, requireFresh: true))
            .Should()
            .Be(
                RunnerRequestVerdict.Replayed,
                "a synchronous request's nonce is accepted once (see docs/adr/0006-a-runner-requires-an-authorization-posture.md)"
            );
    }

    [Test]
    public async Task Verify_ForgedSignature_DoesNotConsumeTheNonce()
    {
        // The nonce is remembered only after the MAC verifies, so a caller without the key
        // cannot spend a nonce the scheduler is about to use.
        var verifier = Verifier();
        var genuine = Sign(RunnerRequestPurpose.Run);
        var forged =
            genuine[..genuine.LastIndexOf("s=", StringComparison.Ordinal)]
            + "s="
            + Convert.ToBase64String(new byte[32]);

        (await verifier.VerifyAsync(RunnerRequestPurpose.Run, Body, forged, requireFresh: true))
            .Should()
            .Be(RunnerRequestVerdict.Invalid);
        (await verifier.VerifyAsync(RunnerRequestPurpose.Run, Body, genuine, requireFresh: true))
            .Should()
            .Be(RunnerRequestVerdict.Accepted);
    }

    [Test]
    public async Task Verify_NoSigningKey_AcceptsWithoutASignature() => (
            await Verifier(o =>
                {
                    o.SigningKey = null;
                    o.AllowUnsignedRequests();
                })
                .VerifyAsync(RunnerRequestPurpose.Run, Body, null, requireFresh: true)
        ).Should().Be(RunnerRequestVerdict.Accepted);

    #endregion

    #region Posture

    [Test]
    public void EnsurePosture_NothingConfigured_Throws()
    {
        var verifier = Verifier(o => o.SigningKey = null);

        var act = () => verifier.EnsurePosture("test-entry");

        act.Should()
            .Throw<InvalidOperationException>(
                "an entry point with no posture refuses to start (see docs/adr/0006-a-runner-requires-an-authorization-posture.md)"
            )
            .WithMessage("*no authorization posture*");
    }

    [Test]
    public void EnsurePosture_PolicyOnly_ThrowsWhereAPolicyCannotApply()
    {
        var verifier = Verifier(o =>
        {
            o.SigningKey = null;
            o.AuthorizationPolicy = "scheduler";
        });

        var act = () => verifier.EnsurePosture("sqs");

        act.Should().Throw<InvalidOperationException>();
        verifier
            .Invoking(v => v.EnsurePosture("/trax/run", policyApplies: true))
            .Should()
            .NotThrow();
    }

    [Test]
    public void EnsurePosture_SigningKeyOrUnsignedOptIn_Passes()
    {
        Verifier().Invoking(v => v.EnsurePosture("a")).Should().NotThrow();
        Verifier(o =>
            {
                o.SigningKey = null;
                o.AllowUnsignedRequests();
            })
            .Invoking(v => v.EnsurePosture("b"))
            .Should()
            .NotThrow();
    }

    [Test]
    public void EnsurePosture_UnsignedOptIn_LogsAWarningOncePerEntryPoint()
    {
        var logger = new CapturingLogger();
        var verifier = new RunnerRequestVerifier(
            new TraxJobRunnerOptions().AllowUnsignedRequests(),
            logger
        );

        verifier.EnsurePosture("/trax/run");
        verifier.EnsurePosture("/trax/run");
        verifier.EnsurePosture("/trax/execute");

        logger.Warnings.Should().HaveCount(2);
        logger.Warnings.Should().AllSatisfy(w => w.Should().Contain("AllowUnsignedRequests"));
    }

    private sealed class CapturingLogger
        : Microsoft.Extensions.Logging.ILogger<RunnerRequestVerifier>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            if (logLevel == Microsoft.Extensions.Logging.LogLevel.Warning)
                Warnings.Add(formatter(state, exception));
        }
    }

    [Test]
    public void Options_ShortKey_Throws()
    {
        var act = () =>
            new RunnerRequestVerifier(
                new TraxJobRunnerOptions { SigningKey = new byte[16] },
                NullLogger<RunnerRequestVerifier>.Instance
            );

        act.Should().Throw<ArgumentException>().WithMessage("*at least 32 bytes*");
    }

    #endregion

    #region HTTP senders

    [Test]
    public async Task HttpJobSubmitter_WithSigningKey_SignsTheExactBodySent()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK, """{"metadataId":7}""");
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://test/") };
        var submitter = new HttpJobSubmitter(
            client,
            new RemoteWorkerOptions { BaseUrl = "http://test/", SigningKey = Key },
            NullLogger<HttpJobSubmitter>.Instance
        );

        await submitter.EnqueueAsync(7);

        var (body, signature) = handler.Requests.Single();
        (
            await Verifier()
                .VerifyAsync(RunnerRequestPurpose.Execute, body, signature, requireFresh: true)
        )
            .Should()
            .Be(RunnerRequestVerdict.Accepted);
        JsonSerializer
            .Deserialize<RemoteJobRequest>(
                body,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)
            )!
            .MetadataId.Should()
            .Be(7);
    }

    [Test]
    public async Task HttpJobSubmitter_WithoutSigningKey_SendsNoSignature()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK, """{"metadataId":7}""");
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://test/") };
        var submitter = new HttpJobSubmitter(
            client,
            new RemoteWorkerOptions { BaseUrl = "http://test/" },
            NullLogger<HttpJobSubmitter>.Instance
        );

        await submitter.EnqueueAsync(7);

        handler.Requests.Single().Signature.Should().BeNull();
    }

    [Test]
    public async Task HttpRunExecutor_WithSigningKey_SignsForRun()
    {
        var handler = new CapturingHandler(
            HttpStatusCode.OK,
            JsonSerializer.Serialize(new RemoteRunResponse(MetadataId: 1))
        );
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://test/") };
        var executor = new HttpRunExecutor(
            client,
            new RemoteRunOptions { BaseUrl = "http://test/", SigningKey = Key },
            NullLogger<HttpRunExecutor>.Instance
        );

        await executor.ExecuteAsync(
            "My.Train",
            new { Name = "x" },
            typeof(Trax.Core.Functional.Unit)
        );

        var (body, signature) = handler.Requests.Single();
        (
            await Verifier()
                .VerifyAsync(RunnerRequestPurpose.Run, body, signature, requireFresh: true)
        )
            .Should()
            .Be(RunnerRequestVerdict.Accepted);
    }

    [Test]
    public async Task PostWithRetry_EachAttemptCarriesItsOwnNonce()
    {
        var handler = new CapturingHandler(HttpStatusCode.ServiceUnavailable, "", okAfter: 1);
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://test/") };

        using var response = await HttpRetryHelper.PostWithRetryAsync(
            client,
            new RemoteJobRequest(1),
            new HttpRetryOptions
            {
                MaxRetries = 2,
                BaseDelay = TimeSpan.FromMilliseconds(1),
                MaxDelay = TimeSpan.FromMilliseconds(1),
            },
            null,
            CancellationToken.None,
            Key,
            RunnerRequestPurpose.Execute
        );

        handler.Requests.Should().HaveCount(2);
        var verifier = Verifier();
        foreach (var (body, signature) in handler.Requests)
            (
                await verifier.VerifyAsync(
                    RunnerRequestPurpose.Execute,
                    body,
                    signature,
                    requireFresh: true
                )
            )
                .Should()
                .Be(RunnerRequestVerdict.Accepted);
    }

    private sealed class CapturingHandler(
        HttpStatusCode status,
        string responseBody,
        int okAfter = 0
    ) : HttpMessageHandler
    {
        public List<(byte[] Body, string? Signature)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var body = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
            var signature = request.Headers.TryGetValues(
                RunnerRequestSignature.HeaderName,
                out var values
            )
                ? values.Single()
                : null;
            Requests.Add((body, signature));

            var code = okAfter > 0 && Requests.Count > okAfter ? HttpStatusCode.OK : status;
            return new HttpResponseMessage(code)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            };
        }
    }

    #endregion
}
