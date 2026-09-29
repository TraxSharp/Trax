using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Trax.Api.Auth.ApiKey;

namespace Trax.Api.Tests.Auth;

/// <summary>
/// A key registered through <c>keys.Add</c> whose text carries the
/// <c>do-not-use-in-production</c> marker the Trax templates and samples put on their demo
/// keys starts only in Development. Hashed keys are not inspected: their text never reaches
/// the process.
/// </summary>
[TestFixture]
public class DemoApiKeyEnvironmentTests
{
    [TestCase("demo-key-do-not-use-in-production")]
    [TestCase("ADMIN-KEY-DO-NOT-USE-IN-PRODUCTION")]
    public async Task A_marked_key_refuses_to_start_outside_Development(string key)
    {
        var act = () => StartAsync(Environments.Production, key);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*do-not-use-in-production*Development*");
    }

    [Test]
    public async Task The_refusal_does_not_repeat_the_key()
    {
        var act = () => StartAsync(Environments.Staging, "s3cret-do-not-use-in-production");

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should()
            .NotContain("s3cret");
    }

    [Test]
    public async Task A_marked_key_starts_in_Development()
    {
        var act = () => StartAsync(Environments.Development, "demo-key-do-not-use-in-production");

        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task An_unmarked_key_starts_outside_Development()
    {
        var act = () => StartAsync(Environments.Production, "a-real-key-from-a-secret-store");

        await act.Should().NotThrowAsync();
    }

    private static async Task StartAsync(string environment, string key)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new Environment(environment));
        services.AddLogging();
        services.AddTraxApiKeyAuth(keys => keys.Add(key, id: "demo", "User"));
        await using var provider = services.BuildServiceProvider();

        foreach (var hosted in provider.GetServices<IHostedService>())
            await hosted.StartAsync(CancellationToken.None);
    }

    private sealed class Environment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
