using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.StaticFiles;

namespace Trax.Api.Dashboard.Serving;

/// <summary>
/// The built app's files, by their path relative to its root (<c>index.html</c>,
/// <c>assets/index-3f2a9c.js</c>). The build embeds them in this assembly under
/// <see cref="ResourcePrefix"/>; the project file says when.
/// </summary>
internal sealed class DashboardAssets
{
    /// <summary>The prefix of every embedded file's resource name.</summary>
    internal const string ResourcePrefix = "TraxApiDashboard/";

    /// <summary>The app's entry page, which the host serves with its runtime config written in.</summary>
    internal const string IndexPath = "index.html";

    private static readonly Lazy<DashboardAssets> EmbeddedAssets = new(() =>
    {
        var assembly = typeof(DashboardAssets).Assembly;
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var name in assembly.GetManifestResourceNames())
        {
            if (!name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
                continue;
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            files[name[ResourcePrefix.Length..]] = buffer.ToArray();
        }
        return new DashboardAssets(files);
    });

    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    private readonly Dictionary<string, Asset> _files;

    internal DashboardAssets(IReadOnlyDictionary<string, byte[]> files)
    {
        _files = files.ToDictionary(
            f => f.Key,
            f => new Asset(f.Key, f.Value, ContentTypeOf(f.Key), ETagOf(f.Value)),
            StringComparer.Ordinal
        );
        if (files.TryGetValue(IndexPath, out var index))
            IndexTemplate = Encoding.UTF8.GetString(index);
    }

    /// <summary>The files embedded in this assembly by the build.</summary>
    internal static DashboardAssets Embedded => EmbeddedAssets.Value;

    /// <summary>The entry page as built, or <see langword="null"/> when the build carries no app.</summary>
    internal string? IndexTemplate { get; }

    internal bool TryGet(string path, out Asset asset) => _files.TryGetValue(path, out asset!);

    /// <summary>
    /// Whether a file at <paramref name="path"/> is named by its content: Vite writes every file
    /// it builds under <c>assets/</c> with a hash of its content in the name, so a new build never
    /// reuses a name and the file can be cached for good.
    /// </summary>
    internal static bool IsContentHashed(string path) =>
        path.StartsWith("assets/", StringComparison.Ordinal);

    internal static string ETagOf(byte[] content) =>
        $"\"{Convert.ToHexStringLower(SHA256.HashData(content))[..32]}\"";

    private static string ContentTypeOf(string path) =>
        ContentTypes.TryGetContentType(path, out var type) ? type : "application/octet-stream";

    internal sealed record Asset(string Path, byte[] Content, string ContentType, string ETag);
}
