using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Trax.Scheduler.Sandbox.Launcher.Configuration;
using Trax.Scheduler.Services.Sandbox;

namespace Trax.Scheduler.Sandbox.Launcher.Core;

/// <summary>
/// Reads a hub's request and checks it against the launcher's configuration. The request's shape
/// is fixed by <see cref="SandboxJson.Options"/>, which refuses any member it does not know, so an
/// <c>image</c>, a <c>command</c>, <c>privileged</c> or anything else a compromised hub adds is a
/// refusal, not something ignored. See scheduler/0020.
/// </summary>
internal static partial class SandboxRequestReader
{
    /// <summary>The request, its profile and nothing else; or why it is refused.</summary>
    public static (SandboxRunRequest? Request, SandboxProfile? Profile, string? Refused) Read(
        ReadOnlySpan<byte> body,
        SandboxLauncherOptions options
    )
    {
        SandboxRunRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<SandboxRunRequest>(body, SandboxJson.Options);
        }
        catch (JsonException ex)
        {
            return (
                null,
                null,
                $"the request is not the sandbox request shape ({Plain(ex.Message)})"
            );
        }

        if (request is null)
            return (null, null, "the request is empty");

        var refused = Check(request, options, out var profile);
        return refused is null ? (request, profile, null) : (null, null, refused);
    }

    private static string? Check(
        SandboxRunRequest request,
        SandboxLauncherOptions options,
        out SandboxProfile? profile
    )
    {
        profile = null;
        if (!RunId.IsMatch(request.RunId))
            return "the run id is not 32 lowercase hex characters";
        if (!options.Profiles.TryGetValue(request.Profile, out profile))
            return $"there is no profile '{Plain(request.Profile)}' on this launcher";
        if (!TrainName.IsMatch(request.TrainName))
            return "the train name is not a type name";
        if (Encoding.UTF8.GetByteCount(request.InputJson) > options.Caps.MaxInputBytes)
            return string.Create(
                CultureInfo.InvariantCulture,
                $"the input is larger than {options.Caps.MaxInputBytes} bytes"
            );
        if (!IsJson(request.InputJson))
            return "the input is not JSON";
        return OverCaps(request.Limits, options.Caps);
    }

    /// <summary>Why <paramref name="limits"/> cannot run under <paramref name="caps"/>, or null.</summary>
    internal static string? OverCaps(SandboxLimits limits, SandboxCaps caps)
    {
        if (limits.Invalid() is { } invalid)
            return invalid;
        return limits.CpuMillicores > caps.MaxCpuMillicores
                ? Over("CPU", limits.CpuMillicores, caps.MaxCpuMillicores, "millicores")
            : limits.MemoryMiB > caps.MaxMemoryMiB
                ? Over("memory", limits.MemoryMiB, caps.MaxMemoryMiB, "MiB")
            : limits.DiskMiB > caps.MaxDiskMiB
                ? Over("disk", limits.DiskMiB, caps.MaxDiskMiB, "MiB")
            : limits.TimeoutSeconds > caps.MaxTimeoutSeconds
                ? Over("timeout", limits.TimeoutSeconds, caps.MaxTimeoutSeconds, "seconds")
            : null;
    }

    private static string Over(string what, int asked, int cap, string unit) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"the {what} asked for ({asked} {unit}) is over this launcher's cap ({cap} {unit})"
        );

    private static bool IsJson(string json)
    {
        try
        {
            using var _ = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // A refusal quotes the request back, cut short and without control characters.
    private static string Plain(string text)
    {
        var plain = new string(text.Where(c => !char.IsControl(c)).Take(200).ToArray());
        return plain.Length < text.Length ? plain + "..." : plain;
    }

    [GeneratedRegex("^[0-9a-f]{32}$", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex RunId { get; }

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_.+`\[\],]{0,511}$", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex TrainName { get; }
}
