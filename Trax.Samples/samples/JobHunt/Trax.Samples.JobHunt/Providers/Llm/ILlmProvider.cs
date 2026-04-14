namespace Trax.Samples.JobHunt.Providers.Llm;

public interface ILlmProvider
{
    Task<string> GenerateAsync(string prompt, string model, CancellationToken ct = default);
}
