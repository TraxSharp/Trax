using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Trax.Samples.JobHunt.Providers.Llm;

public class OllamaLlmProvider(HttpClient httpClient, IOptions<OllamaOptions> options)
    : ILlmProvider
{
    public async Task<string> GenerateAsync(
        string prompt,
        string model,
        CancellationToken ct = default
    )
    {
        var baseUrl = options.Value.BaseUrl.TrimEnd('/');

        var request = new
        {
            model,
            prompt,
            stream = false,
        };

        var response = await httpClient.PostAsJsonAsync($"{baseUrl}/api/generate", request, ct);
        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct),
            cancellationToken: ct
        );

        return doc.RootElement.GetProperty("response").GetString()
            ?? throw new InvalidOperationException("Ollama returned null response.");
    }
}
