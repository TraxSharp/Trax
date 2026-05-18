using System.Net;
using System.Net.Http;

namespace Trax.Api.Tests.GraphQLClient.UnitTests;

/// <summary>
/// Returns a canned response for any HTTP request. Used in unit tests that need to inject
/// specific introspection failure modes (errors-in-response, malformed JSON, network failure)
/// without spinning up a real server.
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public StubHttpMessageHandler(string jsonBody, HttpStatusCode status = HttpStatusCode.OK)
    {
        _responder = _ => new HttpResponseMessage(status)
        {
            Content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json"),
        };
    }

    public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _responder = responder;
    }

    public static StubHttpMessageHandler AlwaysThrows(Exception ex) => new(_ => throw ex);

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    ) => Task.FromResult(_responder(request));
}
