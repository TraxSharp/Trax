using System.Text.RegularExpressions;

namespace Trax.Scheduler.Sandbox.Launcher.Configuration;

/// <summary>
/// What a sandbox runs: an image pinned by digest, and optionally the command to start it with.
/// Set in the launcher's configuration at deploy time; a hub names a profile and nothing more.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/scheduler-api/trax-sandbox-launcher">SDK reference</seealso>
public sealed partial class SandboxProfile
{
    /// <summary>The image, as <c>name@sha256:&lt;64 hex&gt;</c>. A tag alone is refused: it can be moved.</summary>
    public string Image { get; set; } = "";

    /// <summary>The command, when the image's own entry point is not the sandbox entry point.</summary>
    public IList<string> Command { get; } = [];

    /// <summary>The user the sandbox runs as, <c>uid:gid</c>, never root. 65532:65532 by default.</summary>
    public string User { get; set; } = "65532:65532";

    /// <summary>Why this profile cannot be used, or null.</summary>
    internal string? Invalid(string name)
    {
        if (!Name.IsMatch(name))
            return $"the profile name '{name}' is not 1 to 63 lowercase letters, digits or '-'";
        if (!Digest.IsMatch(Image))
            return $"profile '{name}' names its image as '{Image}', not name@sha256:<digest>";
        if (Command.Any(string.IsNullOrEmpty))
            return $"profile '{name}' has an empty command argument";
        if (!UserIds.IsMatch(User))
            return $"profile '{name}' runs as '{User}', which is not a non-root uid:gid";
        return null;
    }

    /// <summary>Whether <paramref name="image"/> is pinned by digest.</summary>
    internal static bool IsPinned(string? image) => image is not null && Digest.IsMatch(image);

    [GeneratedRegex(
        "^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?$",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        1000
    )]
    internal static partial Regex Name { get; }

    [GeneratedRegex(
        @"^[a-z0-9][a-z0-9._/:-]*@sha256:[0-9a-f]{64}$",
        RegexOptions.CultureInvariant,
        1000
    )]
    private static partial Regex Digest { get; }

    [GeneratedRegex(
        "^(?<uid>[1-9][0-9]{0,9}):(?<gid>[1-9][0-9]{0,9})$",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        1000
    )]
    internal static partial Regex UserIds { get; }
}
