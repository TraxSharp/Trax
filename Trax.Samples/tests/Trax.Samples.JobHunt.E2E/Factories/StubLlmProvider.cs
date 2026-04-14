using Trax.Samples.JobHunt.Providers.Llm;

namespace Trax.Samples.JobHunt.E2E.Factories;

/// <summary>
/// Deterministic LLM provider for E2E tests. Returns a canned response
/// that includes the model name so tests can verify it was called correctly.
/// </summary>
public class StubLlmProvider : ILlmProvider
{
    public Task<string> GenerateAsync(string prompt, string model, CancellationToken ct = default)
    {
        return Task.FromResult($"# Generated with {model}\n\nStub content for testing.");
    }
}
