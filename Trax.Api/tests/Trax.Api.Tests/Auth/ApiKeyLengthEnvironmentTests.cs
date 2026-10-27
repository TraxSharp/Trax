using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Trax.Api.Auth.ApiKey;

namespace Trax.Api.Tests.Auth;

/// <summary>
/// A key registered through <c>keys.Add</c> shorter than
/// <see cref="ApiKeyBuilder.MinimumKeyLength"/> characters starts only in Development, the way a
/// demo key does. Hashed keys are not inspected: their text never reaches the process.
/// </summary>
[TestFixture]
public class ApiKeyLengthEnvironmentTests
{
    private static readonly string LongEnough = new('k', ApiKeyBuilder.MinimumKeyLength);

    [TestCase("short-key")]
    [TestCase("0123456789012345678901234567890")]
    public async Task A_short_key_refuses_to_start_outside_Development(string key)
    {
        var act = () => StartAsync(Environments.Production, key);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage($"*shorter than {ApiKeyBuilder.MinimumKeyLength} characters*Development*");
    }

    [Test]
    public async Task The_refusal_does_not_repeat_the_key()
    {
        var act = () => StartAsync(Environments.Staging, "s3cret");

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should()
            .NotContain("s3cret");
    }

    [Test]
    public async Task A_short_key_starts_in_Development()
    {
        var act = () => StartAsync(Environments.Development, "short-key");

        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task A_key_of_the_minimum_length_starts_outside_Development()
    {
        var act = () => StartAsync(Environments.Production, LongEnough);

        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task One_short_key_among_long_ones_refuses_to_start()
    {
        var act = () => StartAsync(Environments.Production, LongEnough, LongEnough + "-2", "short");

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Test]
    public async Task A_hashed_key_is_not_inspected()
    {
        var services = Services(Environments.Production);
        services.AddTraxApiKeyAuth(keys =>
            keys.AddHashed(new byte[16], new byte[32], id: "hashed", "User")
        );

        var act = () => StartAllAsync(services);

        await act.Should().NotThrowAsync();
    }

    private static Task StartAsync(string environment, params string[] keys)
    {
        var services = Services(environment);
        services.AddTraxApiKeyAuth(builder =>
        {
            for (var i = 0; i < keys.Length; i++)
                builder.Add(keys[i], id: $"key-{i}", "User");
        });
        return StartAllAsync(services);
    }

    private static ServiceCollection Services(string environment)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new Environment(environment));
        services.AddLogging();
        return services;
    }

    private static async Task StartAllAsync(ServiceCollection services)
    {
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
