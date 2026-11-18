using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Trax.Api.Console.Terminal.Lists;
using Trax.Api.Console.Terminal.Operations;
using Trax.Client;

namespace Trax.Api.Console.Terminal.Connection;

/// <summary>Turns a redirect into a failure, so the credential goes to the endpoint the operator named and no other.</summary>
internal sealed class NoRedirects : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if ((int)response.StatusCode is < 300 or >= 400)
            return response;
        var location = response.Headers.Location;
        response.Dispose();
        throw new HttpRequestException(
            $"{request.RequestUri} redirected to {Shown(request.RequestUri, location)}, and trax-console follows no "
                + "redirect. Give --host and --graphql-path of the endpoint itself."
        );
    }

    // Where it pointed, without userinfo, on one line, and short.
    private static string Shown(Uri? from, Uri? location)
    {
        if (location is null)
            return "nowhere";
        if (!location.IsAbsoluteUri && from is not null)
            location = new Uri(from, location);
        var text = location.IsAbsoluteUri
            ? new UriBuilder(location) { UserName = "", Password = "" }.Uri.ToString()
            : location.OriginalString;
        text = TerminalText.Line(text);
        return text.Length > 200 ? text[..200] + "..." : text;
    }
}
