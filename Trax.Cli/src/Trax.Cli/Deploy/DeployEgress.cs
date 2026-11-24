using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Trax.Cli.Deploy;

/// <summary>
/// A service's <c>egress</c>: the domains it may reach, and the egress image (<c>Trax.Egress</c>) every target that
/// runs containers renders beside it to hold it to them. The numbers here are the image's own
/// (<c>Trax.Egress/cmd/trax-egress/main.go</c>); <c>EgressContractTests</c> holds the two together
/// (<c>Trax.Docs/adr/0064</c>).
/// </summary>
internal static partial class DeployEgress
{
    /// <summary>The uid the egress resolver and proxy run as, which the rules let out. An app must not run as it.</summary>
    public const int Uid = 61000;

    /// <summary>The egress's loopback ports: its resolver and its proxy.</summary>
    public const int DnsPort = 15053;

    /// <inheritdoc cref="DnsPort"/>
    public const int ProxyPort = 15001;

    /// <summary>The init container that applies the rules, the one container the admission policy lets add NET_ADMIN.</summary>
    public const string RulesContainer = "egress-rules";

    /// <summary>The sidecar that runs the resolver and the proxy.</summary>
    public const string SidecarContainer = "egress";

    /// <summary>The image's commands.</summary>
    public const string RulesCommand = "rules";

    /// <inheritdoc cref="RulesCommand"/>
    public const string ServeCommand = "serve";

    /// <inheritdoc cref="RulesCommand"/>
    public const string RunCommand = "run";

    /// <inheritdoc cref="RulesCommand"/>
    public const string ReadyCommand = "ready";

    /// <summary>The image's binary.</summary>
    public const string Binary = "/usr/local/bin/trax-egress";

    /// <summary>The variable the image reads its list from.</summary>
    public const string AllowVariable = "TRAX_EGRESS_ALLOW";

    /// <summary>The variable whose connection string's hosts the image allows: the database, wherever it is.</summary>
    public const string PostgresVariable = "TRAX_EGRESS_POSTGRES";

    /// <summary>Where the released image is published.</summary>
    public const string Repository = "ghcr.io/traxsharp/trax-egress";

    /// <summary>The assembly metadata the Release workflow writes the image's digest reference under.</summary>
    public const string ImageMetadataKey = "TraxEgressImage";

    /// <summary>The most domains one service may name.</summary>
    public const int MaxEntries = 64;

    /// <summary>The port a domain without one is reached on.</summary>
    public const int DefaultPort = 443;

    /// <summary>EKS Pod Identity's agent, which a pod on EKS reads its role's credentials from.</summary>
    public const string PodIdentityAgent = "169.254.170.23:80";

    /// <summary>
    /// The first problem with <paramref name="service"/>'s entries, as a sentence, or null when they are all domains
    /// the image accepts: lower-case labels of letters, digits and dashes, at least two of them, no wildcard, scheme,
    /// path or address, and a port from 1 to 65535 when one is given. Each domain may be named once.
    /// </summary>
    public static string? Check(string service, IReadOnlyList<string>? entries)
    {
        if (entries is null)
            return null;
        if (entries.Count > MaxEntries)
            return string.Create(
                CultureInfo.InvariantCulture,
                $"service '{service}' names {entries.Count} egress domains; at most {MaxEntries}."
            );
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (Normalize(entry) is not { } normal)
                return $"service '{service}' has egress '{entry}', which is not a domain. Name a domain, with a port "
                    + "when it is not 443 (api.example.com, registry.example.com:8443): lower case, no wildcard, "
                    + "scheme, path or address. The deployment's own database and broker are allowed without "
                    + "naming them.";
            if (!seen.Add(normal))
                return $"service '{service}' names egress '{entry}' twice.";
        }
        return null;
    }

    /// <summary><paramref name="entry"/> as <c>name:port</c>, or null when it is not a domain the image accepts.</summary>
    public static string? Normalize(string? entry)
    {
        if (string.IsNullOrEmpty(entry))
            return null;
        var host = entry;
        var port = DefaultPort;
        var colon = entry.IndexOf(':', StringComparison.Ordinal);
        if (colon >= 0)
        {
            host = entry[..colon];
            var text = entry[(colon + 1)..];
            if (
                !int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out port)
                || port is < 1 or > 65535
                || !string.Equals(
                    port.ToString(CultureInfo.InvariantCulture),
                    text,
                    StringComparison.Ordinal
                )
            )
                return null;
        }
        return IsDomain(host)
            ? string.Create(CultureInfo.InvariantCulture, $"{host}:{port}")
            : null;
    }

    /// <summary>
    /// Whether <paramref name="host"/> is a lower-case domain of at least two labels, at most 253 characters, whose
    /// last label is not all digits (so an address is never one).
    /// </summary>
    public static bool IsDomain(string host)
    {
        if (host.Length is 0 or > 253 || !Domain.IsMatch(host))
            return false;
        var last = host[(host.LastIndexOf('.') + 1)..];
        return !last.All(char.IsAsciiDigit);
    }

    /// <summary>
    /// The list the image is given: the service's domains, then the deployment's own services it must still reach,
    /// sorted and each once, separated by spaces.
    /// </summary>
    public static string Allow(IEnumerable<string> domains, IEnumerable<string> own) =>
        string.Join(
            ' ',
            domains
                .Select(d => Normalize(d) ?? d)
                .Concat(own)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
        );

    /// <summary>A TCP entry for one of the deployment's own services: <c>tcp:host:port</c>.</summary>
    public static string Own(string host, int port) =>
        string.Create(CultureInfo.InvariantCulture, $"tcp:{host}:{port}");

    /// <summary>
    /// The image to run: the file's <c>egressImage</c>, else the one this trax was released with. A development
    /// build names none, and a file that uses egress must name one.
    /// </summary>
    public static string Image(DeployFile file)
    {
        var image =
            file.EgressImage
            ?? BuiltIn
            ?? throw new InvalidOperationException(
                "A service names egress, and this trax names no egress image: it is a development build, not a "
                    + "release. Name one by digest in trax.deploy.json, as \"egressImage\": "
                    + $"\"{Repository}@sha256:<digest>\"."
            );
        return IsDigestReference(image)
            ? image
            : throw new InvalidOperationException(
                $"The egress image '{image}' is not named by digest (<registry>/<path>@sha256:<digest>)."
            );
    }

    /// <summary>The image the Release workflow packed into this build, or null in a development build.</summary>
    public static string? BuiltIn =>
        typeof(DeployEgress)
            .Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => string.Equals(a.Key, ImageMetadataKey, StringComparison.Ordinal))
            ?.Value
            is { Length: > 0 } image
            ? image
            : null;

    /// <summary>
    /// Whether <paramref name="image"/> names an image by digest (<c>registry/path@sha256:&lt;64 hex&gt;</c>), so
    /// what runs cannot change after the file is written.
    /// </summary>
    public static bool IsDigestReference(string image) => DigestReference.IsMatch(image);

    [GeneratedRegex(
        "^(?=.{1,253}$)[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?(\\.[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?)+$",
        RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 1000
    )]
    private static partial Regex Domain { get; }

    [GeneratedRegex(
        "^[a-z0-9][a-z0-9.-]*(:[0-9]{1,5})?(/[a-z0-9]([a-z0-9._-]*[a-z0-9])?)+@sha256:[0-9a-f]{64}$",
        RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 1000
    )]
    private static partial Regex DigestReference { get; }
}
