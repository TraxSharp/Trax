using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Trax.Samples.JobHunt.Providers.Llm;

namespace Trax.Samples.JobHunt.Tests.UnitTests.Providers.Llm;

[TestFixture]
public class OllamaLlmProviderTests
{
    [Test]
    public async Task Generate_HappyPath_ReturnsResponseText()
    {
        var handler = new FakeHandler(
            JsonSerializer.Serialize(new { response = "Generated text here" })
        );
        var provider = CreateProvider(handler);

        var result = await provider.GenerateAsync("test prompt", "llama3.1:8b");

        result.Should().Be("Generated text here");
    }

    [Test]
    public void Generate_ServerError_ThrowsHttpRequestException()
    {
        var handler = new FakeHandler(statusCode: HttpStatusCode.InternalServerError);
        var provider = CreateProvider(handler);

        var act = () => provider.GenerateAsync("test", "llama3.1:8b");

        act.Should().ThrowAsync<HttpRequestException>();
    }

    [Test]
    public void Generate_Timeout_ThrowsTaskCanceledException()
    {
        var handler = new FakeHandler(delay: TimeSpan.FromHours(1));
        var provider = CreateProvider(handler);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        var act = () => provider.GenerateAsync("test", "llama3.1:8b", cts.Token);

        act.Should().ThrowAsync<TaskCanceledException>();
    }

    private static OllamaLlmProvider CreateProvider(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        var options = Options.Create(new OllamaOptions { BaseUrl = "http://localhost:11434" });
        return new OllamaLlmProvider(httpClient, options);
    }

    private sealed class FakeHandler(
        string? responseBody = null,
        HttpStatusCode statusCode = HttpStatusCode.OK,
        TimeSpan? delay = null
    ) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            if (delay.HasValue)
                await Task.Delay(delay.Value, cancellationToken);

            return new HttpResponseMessage(statusCode)
            {
                Content = responseBody is not null
                    ? new StringContent(responseBody, Encoding.UTF8, "application/json")
                    : null,
            };
        }
    }
}
