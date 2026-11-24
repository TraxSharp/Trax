using Trax.Scheduler.Services.Http;
using Trax.Scheduler.Services.Sandbox;

namespace Trax.Scheduler.Configuration;

/// <summary>
/// How a hub reaches its sandbox launcher, set with <c>AddTraxSandbox</c>.
/// </summary>
/// <remarks>
/// Exactly one of <see cref="LauncherSocketPath"/> (one host) and <see cref="LauncherUrl"/> (a
/// cluster) is set, and <see cref="SigningKey"/> always is: the launcher refuses an unsigned
/// request, and the hub reads only answers the launcher signed for its request.
/// </remarks>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/scheduler-api/add-trax-sandbox">SDK reference</seealso>
public sealed class SandboxClientOptions
{
    /// <summary>The default most bytes of an answer: 128 MiB, room for the default archive in base64.</summary>
    public const long DefaultMaxResponseBodyBytes = 128L * 1024 * 1024;

    /// <summary>
    /// The launcher's Unix socket, on one host. The socket's file permissions decide which local
    /// users may connect; the signature decides which of them are the hub.
    /// </summary>
    public string? LauncherSocketPath { get; set; }

    /// <summary>
    /// The launcher's base URL, on a cluster. <c>https://</c> unless its host is loopback or
    /// <see cref="AllowInsecureTransport"/> is set.
    /// </summary>
    public string? LauncherUrl { get; set; }

    /// <summary>
    /// Accepts an <c>http://</c> <see cref="LauncherUrl"/> to a host that is not loopback. Every
    /// answer is signed, so it cannot be changed on the way, but the input and the output cross in
    /// cleartext. Set it only where the network between hub and launcher is trusted.
    /// </summary>
    public bool AllowInsecureTransport { get; set; }

    /// <summary>The key shared with the launcher, at least 32 bytes.</summary>
    public byte[]? SigningKey { get; set; }

    /// <summary>The most bytes of a launcher answer read; a larger one fails the run unread.</summary>
    public long MaxResponseBodyBytes { get; set; } = DefaultMaxResponseBodyBytes;

    /// <summary>
    /// The most file bytes, and files, accepted from a sandbox's output, checked again here after
    /// the launcher checked them. Keep them at or above the launcher's.
    /// </summary>
    public SandboxArchiveLimits Archive { get; set; } = new(64L * 1024 * 1024, 10_000);

    /// <summary>
    /// How long past the train's own timeout the hub waits for the launcher's answer: the time to
    /// start the sandbox and collect its output. Two minutes by default.
    /// </summary>
    public TimeSpan ResponseGrace { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Throws <see cref="InvalidOperationException"/> when the options cannot reach a launcher safely.</summary>
    public void Validate()
    {
        if (string.IsNullOrEmpty(LauncherSocketPath) == string.IsNullOrEmpty(LauncherUrl))
            throw new InvalidOperationException(
                "Set exactly one of SandboxClientOptions.LauncherSocketPath (one host) and "
                    + "SandboxClientOptions.LauncherUrl (a cluster)."
            );

        if (LauncherUrl is not null)
        {
            if (
                !Uri.TryCreate(LauncherUrl, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("http" or "https")
            )
                throw new InvalidOperationException(
                    "SandboxClientOptions.LauncherUrl must be an absolute http(s) URL."
                );
            RemoteEndpointTransport.Ensure(
                LauncherUrl,
                AllowInsecureTransport,
                nameof(SandboxClientOptions)
            );
        }

        if (LauncherSocketPath is not null && !Path.IsPathFullyQualified(LauncherSocketPath))
            throw new InvalidOperationException(
                "SandboxClientOptions.LauncherSocketPath must be an absolute path."
            );

        try
        {
            SandboxChannelSignature.EnsureKey(SigningKey, nameof(SigningKey));
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException(
                "SandboxClientOptions.SigningKey is required. " + ex.Message,
                ex
            );
        }

        if (MaxResponseBodyBytes <= 0)
            throw new InvalidOperationException(
                "SandboxClientOptions.MaxResponseBodyBytes must be positive."
            );
        if (Archive is null || Archive.MaxBytes <= 0 || Archive.MaxFiles <= 0)
            throw new InvalidOperationException(
                "SandboxClientOptions.Archive must have positive limits."
            );
        if (ResponseGrace <= TimeSpan.Zero)
            throw new InvalidOperationException(
                "SandboxClientOptions.ResponseGrace must be positive."
            );
    }
}
