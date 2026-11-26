using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Trax.Scheduler.Services.Sandbox;

/// <summary>
/// The JSON options of the sandbox wire, fixed by Trax and read-only, so neither end depends on
/// its host's settings.
/// </summary>
/// <remarks>
/// Strict on purpose: a member either end does not know is an error, not ignored. A hub that
/// adds <c>"image"</c> or <c>"privileged"</c> to a request is refused for it, which is what keeps
/// the request's shape fixed (see scheduler/0020). Public because the launcher ships in its own
/// package.
/// </remarks>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/scheduler-api/add-trax-sandbox#the-wire">SDK reference</seealso>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class SandboxJson
{
    /// <summary>Web defaults, enums by name, unknown members refused, required members required.</summary>
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true,
            AllowTrailingCommas = false,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 8,
            Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
        };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
