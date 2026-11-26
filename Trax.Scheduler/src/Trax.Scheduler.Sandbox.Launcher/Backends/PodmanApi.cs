using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Trax.Scheduler.Sandbox.Launcher.Backends;

/// <summary>The few calls of podman's libpod REST API the launcher makes, over its Unix socket.</summary>
internal sealed class PodmanApi(HttpClient http)
{
    private const string Prefix = "/v5.0.0/libpod";
    private const int MaxErrorLength = 500;

    public Task CreateVolumeAsync(
        string name,
        IReadOnlyDictionary<string, string> options,
        IReadOnlyDictionary<string, string> labels,
        CancellationToken ct
    ) =>
        SendAsync(
            HttpMethod.Post,
            "/volumes/create",
            new JsonObject
            {
                ["Name"] = name,
                ["Driver"] = "local",
                ["Options"] = ToObject(options),
                ["Labels"] = ToObject(labels),
            },
            ct
        );

    public Task RemoveVolumeAsync(string name, CancellationToken ct) =>
        SendAsync(
            HttpMethod.Delete,
            $"/volumes/{Escape(name)}?force=true",
            body: null,
            ct,
            allowNotFound: true
        );

    /// <summary>Creates a pod with no infra container and nothing shared, only for the cgroup it gives its containers.</summary>
    public Task CreatePodAsync(
        string name,
        IReadOnlyDictionary<string, string> labels,
        CancellationToken ct
    ) =>
        SendAsync(
            HttpMethod.Post,
            "/pods/create",
            new JsonObject
            {
                ["name"] = name,
                ["no_infra"] = true,
                ["labels"] = ToObject(labels),
            },
            ct
        );

    public Task RemovePodAsync(string name, CancellationToken ct) =>
        SendAsync(
            HttpMethod.Delete,
            $"/pods/{Escape(name)}?force=true&t=0",
            body: null,
            ct,
            allowNotFound: true
        );

    public async Task<string> CreateContainerAsync(JsonObject spec, CancellationToken ct)
    {
        using var response = await SendKeepAsync(HttpMethod.Post, "/containers/create", spec, ct)
            .ConfigureAwait(false);
        var created = await response
            .Content.ReadFromJsonAsync<JsonObject>(ct)
            .ConfigureAwait(false);
        return created?["Id"]?.GetValue<string>()
            ?? throw new PodmanApiException(500, "create returned no id");
    }

    public Task StartAsync(string id, CancellationToken ct) =>
        SendAsync(HttpMethod.Post, $"/containers/{Escape(id)}/start", body: null, ct);

    /// <summary>Waits until the container exits and returns its exit code.</summary>
    public async Task<int> WaitAsync(string id, CancellationToken ct)
    {
        using var response = await SendKeepAsync(
                HttpMethod.Post,
                $"/containers/{Escape(id)}/wait?condition=exited",
                body: null,
                ct
            )
            .ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return int.Parse(text.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
    }

    public Task KillAsync(string id, CancellationToken ct) =>
        SendAsync(
            HttpMethod.Post,
            $"/containers/{Escape(id)}/kill?signal=SIGKILL",
            body: null,
            ct,
            allowNotFound: true,
            allowConflict: true
        );

    public async Task<JsonNode?> InspectAsync(string id, CancellationToken ct)
    {
        using var response = await SendKeepAsync(
                HttpMethod.Get,
                $"/containers/{Escape(id)}/json",
                body: null,
                ct
            )
            .ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<JsonNode>(ct).ConfigureAwait(false);
    }

    public async Task PutArchiveAsync(string id, string path, byte[] tar, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            Url($"/containers/{Escape(id)}/archive?path={Uri.EscapeDataString(path)}")
        )
        {
            Content = new ByteArrayContent(tar),
        };
        request.Content.Headers.ContentType = new("application/x-tar");
        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        await EnsureAsync(response, allowNotFound: false, allowConflict: false, ct)
            .ConfigureAwait(false);
    }

    /// <summary>The tar podman makes of <paramref name="path"/> in a running container; the caller reads and disposes it.</summary>
    public async Task<HttpResponseMessage> GetArchiveAsync(
        string id,
        string path,
        CancellationToken ct
    )
    {
        var response = await http.GetAsync(
                Url($"/containers/{Escape(id)}/archive?path={Uri.EscapeDataString(path)}"),
                HttpCompletionOption.ResponseHeadersRead,
                ct
            )
            .ConfigureAwait(false);
        try
        {
            await EnsureAsync(response, allowNotFound: false, allowConflict: false, ct)
                .ConfigureAwait(false);
            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    public Task RemoveContainerAsync(string id, CancellationToken ct) =>
        SendAsync(
            HttpMethod.Delete,
            $"/containers/{Escape(id)}?force=true&v=true&t=0",
            body: null,
            ct,
            allowNotFound: true
        );

    /// <summary>The names of the containers, or of the volumes, carrying <paramref name="label"/>.</summary>
    public Task<IReadOnlyList<string>> ListAsync(
        bool volumes,
        string label,
        CancellationToken ct
    ) => ListAsync(volumes ? PodmanKind.Volume : PodmanKind.Container, label, ct);

    /// <summary>The ids of the containers or pods, or the names of the volumes, carrying <paramref name="label"/>.</summary>
    public async Task<IReadOnlyList<string>> ListAsync(
        PodmanKind kind,
        string label,
        CancellationToken ct
    )
    {
        var filters = Uri.EscapeDataString(
            JsonSerializer.Serialize(
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["label"] = [label] }
            )
        );
        var path = kind switch
        {
            PodmanKind.Volume => $"/volumes/json?filters={filters}",
            PodmanKind.Pod => $"/pods/json?filters={filters}",
            _ => $"/containers/json?all=true&filters={filters}",
        };
        using var response = await SendKeepAsync(HttpMethod.Get, path, body: null, ct)
            .ConfigureAwait(false);
        var items =
            await response.Content.ReadFromJsonAsync<JsonArray>(ct).ConfigureAwait(false) ?? [];
        return items
            .Select(i =>
                kind == PodmanKind.Volume
                    ? i?["Name"]?.GetValue<string>()
                    : i?["Id"]?.GetValue<string>()
            )
            .OfType<string>()
            .ToList();
    }

    private async Task SendAsync(
        HttpMethod method,
        string path,
        JsonNode? body,
        CancellationToken ct,
        bool allowNotFound = false,
        bool allowConflict = false
    )
    {
        using var response = await SendKeepAsync(
                method,
                path,
                body,
                ct,
                allowNotFound,
                allowConflict
            )
            .ConfigureAwait(false);
    }

    /// <summary>Sends a call and returns its response, for the caller to read and dispose.</summary>
    private async Task<HttpResponseMessage> SendKeepAsync(
        HttpMethod method,
        string path,
        JsonNode? body,
        CancellationToken ct,
        bool allowNotFound = false,
        bool allowConflict = false
    )
    {
        using var request = new HttpRequestMessage(method, Url(path));
        if (body is not null)
            request.Content = JsonContent.Create(body);
        var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        try
        {
            await EnsureAsync(response, allowNotFound, allowConflict, ct).ConfigureAwait(false);
            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    private static async Task EnsureAsync(
        HttpResponseMessage response,
        bool allowNotFound,
        bool allowConflict,
        CancellationToken ct
    )
    {
        if (
            response.IsSuccessStatusCode
            || (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
            || (allowConflict && response.StatusCode == HttpStatusCode.Conflict)
        )
            return;

        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        string message;
        try
        {
            message = JsonNode.Parse(text)?["message"]?.GetValue<string>() ?? text;
        }
        catch (JsonException)
        {
            message = text;
        }
        throw new PodmanApiException(
            (int)response.StatusCode,
            message.Length > MaxErrorLength ? message[..MaxErrorLength] : message
        );
    }

    private static Uri Url(string path) => new(Prefix + path, UriKind.Relative);

    private static string Escape(string segment) => Uri.EscapeDataString(segment);

    private static JsonObject ToObject(IReadOnlyDictionary<string, string> values)
    {
        var o = new JsonObject();
        foreach (var (k, v) in values)
            o[k] = v;
        return o;
    }
}
