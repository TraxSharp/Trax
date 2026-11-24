using System.Net;
using System.Text.Json;
using Trax.Scheduler.Services.Sandbox;

namespace Trax.Scheduler.Sandbox.Tests.Fakes;

/// <summary>
/// A launcher on the other end of the hub's HttpClient: checks the request's signature, answers
/// with what the test says, and signs or mis-signs the answer as the test says.
/// </summary>
public sealed class FakeLauncher(byte[] key) : HttpMessageHandler
{
    public Func<SandboxRunRequest, SandboxRunResponse> Answer { get; set; } =
        r => new SandboxRunResponse(
            r.RunId,
            SandboxOutcome.Completed,
            ResultJson: "{\"metadataId\":0}"
        );

    /// <summary>Signs answers with this key instead; null signs with the shared one.</summary>
    public byte[]? AnswerKey { get; set; }

    /// <summary>Leaves the answer unsigned.</summary>
    public bool Unsigned { get; set; }

    /// <summary>Signs the answer as if for another request.</summary>
    public bool SignForAnotherRequest { get; set; }

    /// <summary>Answers with these bytes instead of the serialized answer.</summary>
    public byte[]? RawBody { get; set; }

    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

    public Exception? Throw { get; set; }

    public IList<string> RequestBodies { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        if (Throw is not null)
            throw Throw;

        var body = await (
            request.Content?.ReadAsByteArrayAsync(cancellationToken)
            ?? Task.FromResult(Array.Empty<byte>())
        ).ConfigureAwait(false);
        RequestBodies.Add(System.Text.Encoding.UTF8.GetString(body));
        var signature = request.Headers.GetValues(SandboxChannelSignature.HeaderName).Single();
        SandboxChannelSignature
            .TryVerify(key, body, signature, out _, out _)
            .Should()
            .BeTrue("the hub signs every request");

        var parsed =
            JsonSerializer.Deserialize<SandboxRunRequest>(body, SandboxJson.Options)
            ?? throw new InvalidOperationException();
        var bytes =
            RawBody ?? JsonSerializer.SerializeToUtf8Bytes(Answer(parsed), SandboxJson.Options);
        var response = new HttpResponseMessage(Status) { Content = new ByteArrayContent(bytes) };
        if (!Unsigned)
        {
            var signedFor = SignForAnotherRequest
                ? SandboxChannelSignature.Sign(key, body, 1, "ffffffffffffffffffffffffffffffff")
                : signature;
            response.Headers.TryAddWithoutValidation(
                SandboxChannelSignature.ResponseHeaderName,
                SandboxChannelSignature.SignResponse(AnswerKey ?? key, signedFor, bytes)
            );
        }
        return response;
    }
}
