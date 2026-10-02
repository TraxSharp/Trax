using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using ChilliCream.Nitro.App;
using FluentAssertions;
using HotChocolate.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.Services.HealthCheck;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.Tests;

/// <summary>
/// The GraphQL IDE is served from the assets bundled in the package, so opening it makes no
/// request beyond the host. The page and every asset it references come from the host, and the
/// server sends nothing out while serving them.
/// </summary>
/// <remarks>
/// Runs over a <see cref="TestServer"/>, whose client makes no socket connections of its own, so
/// any outgoing HTTP request seen while the IDE is served was made by the server.
/// </remarks>
[TestFixture]
[NonParallelizable]
public partial class GraphQLIdeAssetsTests
{
    [Test]
    public async Task Ide_ServeMode_IsEmbedded()
    {
        await using var app = await StartAsync();

        var options = app
            .Services.GetRequiredService<IOptionsMonitor<GraphQLServerOptions>>()
            .Get("trax");

        options
            .Tool.ServeMode.Should()
            .BeSameAs(ServeMode.Embedded, "the IDE is served from the bundled package");
    }

    [Test]
    public async Task Ide_PageAndAssets_AreServedWithoutAnOutgoingRequest()
    {
        await using var app = await StartAsync();
        using var outgoing = new OutgoingRequestRecorder();

        var page = await GetAsync(app, "/trax/graphql/", "text/html");
        page.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await page.Content.ReadAsStringAsync();

        var assets = AssetReference()
            .Matches(html)
            .Select(m => m.Groups["path"].Value)
            .Distinct()
            .ToList();
        assets.Should().NotBeEmpty("the IDE page references its scripts and styles");
        assets
            .Should()
            .OnlyContain(
                a => !a.Contains("://") && !a.StartsWith("//"),
                "every asset the IDE page references is served by the host"
            );

        foreach (var asset in assets)
        {
            var response = await GetAsync(app, "/trax/graphql/" + asset.TrimStart('.', '/'), "*/*");
            response.StatusCode.Should().Be(HttpStatusCode.OK, asset);
        }

        outgoing.Requests.Should().BeEmpty("the IDE and its assets come from the package");
    }

    [GeneratedRegex("""(?:src|href)="(?<path>[^"]+\.(?:js|css))""")]
    private static partial Regex AssetReference();

    private static async Task<HttpResponseMessage> GetAsync(
        WebApplication app,
        string path,
        string accept
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
        return await app.GetTestClient().SendAsync(request);
    }

    private static async Task<WebApplication> StartAsync()
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = Environments.Development }
        );
        builder.WebHost.UseTestServer();
        // Development turns on build-time validation; the stubs below do not satisfy the whole
        // graph, and nothing here resolves the services it would complain about.
        builder.Host.UseDefaultServiceProvider(o => o.ValidateOnBuild = false);
        var services = builder.Services;
        services.AddLogging();
        services.AddRouting();
        services.AddSingleton<TraxMarker>();
        services.AddSingleton(Substitute.For<ITrainDiscoveryService>());
        services.AddSingleton(Substitute.For<IEffectRegistry>());
        services.AddSingleton(Substitute.For<ITraxScheduler>());
        services.AddSingleton(Substitute.For<ITraxHealthService>());
        services.AddDbContext<OrderTestDbContext>(o =>
            o.UseInMemoryDatabase("Ide_" + Guid.NewGuid())
        );
        services.AddTraxGraphQL(g => g.AddDbContext<OrderTestDbContext>());

        var app = builder.Build();
        app.UseRouting();
        app.UseTraxGraphQL();
        await app.StartAsync();
        return app;
    }

    /// <summary>
    /// Records every HTTP request the process sends to a host other than loopback, from the
    /// diagnostic events every <see cref="System.Net.Http.SocketsHttpHandler"/> raises.
    /// </summary>
    private sealed class OutgoingRequestRecorder
        : IObserver<DiagnosticListener>,
            IObserver<KeyValuePair<string, object?>>,
            IDisposable
    {
        private readonly ConcurrentQueue<string> _requests = new();
        private readonly List<IDisposable> _subscriptions = [];

        public OutgoingRequestRecorder() =>
            _subscriptions.Add(DiagnosticListener.AllListeners.Subscribe(this));

        public IReadOnlyCollection<string> Requests => _requests;

        public void OnNext(DiagnosticListener listener)
        {
            if (listener.Name == "HttpHandlerDiagnosticListener")
                lock (_subscriptions)
                    _subscriptions.Add(listener.Subscribe(this));
        }

        public void OnNext(KeyValuePair<string, object?> evt)
        {
            if (
                evt.Key != "System.Net.Http.Request"
                && evt.Key != "System.Net.Http.HttpRequestOut.Start"
            )
                return;

            var request =
                evt.Value?.GetType().GetProperty("Request")?.GetValue(evt.Value)
                as HttpRequestMessage;
            var uri = request?.RequestUri;
            if (uri is null || !uri.IsLoopback)
                _requests.Enqueue(uri?.ToString() ?? "(unknown)");
        }

        public void OnCompleted() { }

        public void OnError(Exception error) { }

        public void Dispose()
        {
            lock (_subscriptions)
                foreach (var s in _subscriptions)
                    s.Dispose();
        }
    }
}
