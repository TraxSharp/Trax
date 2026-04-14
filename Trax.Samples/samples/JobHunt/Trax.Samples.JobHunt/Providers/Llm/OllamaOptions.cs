namespace Trax.Samples.JobHunt.Providers.Llm;

public class OllamaOptions
{
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string ResumeModel { get; set; } = "llama3.1:8b";
    public string CoverLetterModel { get; set; } = "llama3.1:8b";
}
