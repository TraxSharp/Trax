using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Trax.Api.Dashboard.Serving;

/// <summary>
/// Answers every request under the dashboard's path: the app's files, its entry page with the
/// runtime config written in, and that page again for any route of the app.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>The bare path redirects to itself with a trailing slash, so the page's relative URLs
/// resolve under it.</item>
/// <item>A path whose last segment has no extension is a route of the app (a deep link), answered
/// with the entry page. A path that looks like a file is answered only with that file, and is a
/// 404 when the build has none: a missing script must never come back as HTML.</item>
/// <item>The entry page is revalidated on every load (<c>no-cache</c>, with an ETag), so a new
/// build is picked up at once; files under <c>assets/</c> are named by their content and cached
/// for good.</item>
/// <item>Every response carries the security headers in <see cref="SetSecurityHeaders"/>.</item>
/// </list>
/// </remarks>
internal sealed partial class DashboardEndpoint(DashboardAssets assets, Func<string> graphQLPath)
{
    /// <summary>The route value the catch-all segment of the dashboard's route binds to.</summary>
    internal const string PathRouteValue = "dashboardPath";

    private const string ImmutableCache = "public, max-age=31536000, immutable";
    private const string Revalidate = "no-cache";

    private static readonly JsonSerializerOptions ConfigJson = new(JsonSerializerDefaults.Web);

    public async Task HandleAsync(HttpContext context)
    {
        var response = context.Response;
        SetSecurityHeaders(context);

        var request = context.Request;
        var relative = context.GetRouteValue(PathRouteValue) as string ?? "";
        var path = request.Path.Value ?? "";

        if (relative.Length == 0 && !path.EndsWith('/'))
        {
            response.Redirect(
                $"{request.PathBase.Add(request.Path).ToUriComponent()}/{request.QueryString}"
            );
            return;
        }

        // The app's base path as the browser sees it: everything the catch-all did not take.
        if (!path.EndsWith(relative, StringComparison.Ordinal))
        {
            response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        var basePath = request.PathBase.Add(new PathString(path[..^relative.Length].TrimEnd('/')));

        if (
            relative.Length == 0
            || relative == DashboardAssets.IndexPath
            || !LooksLikeAFile(relative)
        )
        {
            await WriteIndexAsync(context, basePath);
            return;
        }

        if (!assets.TryGet(relative, out var asset))
        {
            response.StatusCode = StatusCodes.Status404NotFound;
            response.Headers.CacheControl = Revalidate;
            return;
        }

        await WriteAsync(
            context,
            asset.Content,
            asset.ContentType,
            asset.ETag,
            DashboardAssets.IsContentHashed(asset.Path) ? ImmutableCache : Revalidate
        );
    }

    /// <summary>
    /// The entry page with a <c>&lt;base href&gt;</c> at the app's base path, which the build's
    /// relative URLs resolve against, and the runtime config the app reads at startup
    /// (<c>src/lib/runtimeConfig.ts</c>), both written first in its <c>&lt;head&gt;</c>.
    /// </summary>
    internal static string RenderIndex(string template, PathString basePath, string graphQLPath)
    {
        var baseHref = basePath.Add(new PathString("/")).ToUriComponent();
        var config = JsonSerializer.Serialize(
            new { basePath = baseHref, graphqlPath = graphQLPath },
            ConfigJson
        );
        var head =
            $"<base href=\"{HtmlEncoder.Default.Encode(baseHref)}\" />"
            + $"<meta name=\"trax-dashboard-config\" content=\"{HtmlEncoder.Default.Encode(config)}\" />";

        var at = HeadTag().Match(template);
        if (!at.Success)
            throw new InvalidOperationException(
                "The Trax API dashboard's index.html has no <head>, so its runtime config cannot be written into it."
            );
        return template.Insert(at.Index + at.Length, head);
    }

    private async Task WriteIndexAsync(HttpContext context, PathString basePath)
    {
        var graphQL = context.Request.PathBase.Add(new PathString(graphQLPath())).ToUriComponent();
        var html = Encoding.UTF8.GetBytes(RenderIndex(assets.IndexTemplate!, basePath, graphQL));
        await WriteAsync(
            context,
            html,
            "text/html; charset=utf-8",
            DashboardAssets.ETagOf(html),
            Revalidate
        );
    }

    private static async Task WriteAsync(
        HttpContext context,
        byte[] content,
        string contentType,
        string etag,
        string cacheControl
    )
    {
        var response = context.Response;
        response.Headers.CacheControl = cacheControl;
        response.Headers.ETag = etag;

        if (
            context.Request.Headers.IfNoneMatch.ToString() is { Length: > 0 } match
            && match.Split(',').Any(m => m.Trim() == etag)
        )
        {
            response.StatusCode = StatusCodes.Status304NotModified;
            return;
        }

        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = contentType;
        response.ContentLength = content.Length;
        if (!HttpMethods.IsHead(context.Request.Method))
            await response.Body.WriteAsync(content, context.RequestAborted);
    }

    /// <summary>
    /// The headers every dashboard response carries. The policy allows what the app needs and
    /// nothing else: its own scripts, styles and fonts, images from itself or inline data, requests
    /// to its own origin (the GraphQL endpoint), and the subscription socket to the same host.
    /// It cannot be framed, its URLs are not sent to anyone, and nothing is sniffed.
    /// </summary>
    internal static void SetSecurityHeaders(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers.ContentSecurityPolicy = ContentSecurityPolicy(context.Request);
    }

    internal static string ContentSecurityPolicy(HttpRequest request)
    {
        // 'self' covers a socket to the same host in current browsers (CSP Level 3); the explicit
        // ws/wss source covers older ones. A host header that is not a plain host is left out,
        // so nothing a client sends can widen the policy.
        var socket = "";
        var host = request.Host.Value;
        if (host is not null && PlainHost().IsMatch(host))
            socket = request.IsHttps ? $" wss://{host}" : $" ws://{host} wss://{host}";

        return "default-src 'none'; "
            + "script-src 'self'; "
            + "style-src 'self'; "
            + "img-src 'self' data:; "
            + "font-src 'self'; "
            + $"connect-src 'self'{socket}; "
            + "base-uri 'self'; "
            + "form-action 'self'; "
            + "frame-ancestors 'none'; "
            + "object-src 'none'";
    }

    private static bool LooksLikeAFile(string relative)
    {
        var last = relative[(relative.LastIndexOf('/') + 1)..];
        return last.Contains('.');
    }

    [GeneratedRegex("<head(?:\\s[^>]*)?>", RegexOptions.IgnoreCase)]
    private static partial Regex HeadTag();

    [GeneratedRegex("^[A-Za-z0-9.\\-]+(?::[0-9]{1,5})?$|^\\[[0-9A-Fa-f:.]+\\](?::[0-9]{1,5})?$")]
    private static partial Regex PlainHost();
}
