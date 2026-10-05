using AwesomeAssertions;
using Bunit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;
using Radzen;
using Radzen.Blazor;
using Trax.Dashboard.Components.Pages.Settings;
using Trax.Dashboard.Extensions;
using Trax.Dashboard.Tests.Integration.Fakes.Data;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Mediator.Configuration;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.Operations;

namespace Trax.Dashboard.Tests.Integration.UnitTests.Components;

/// <summary>
/// A log level saved on the Server Settings page is the level the host's loggers use, whichever
/// <c>AddTraxDashboard</c> overload the host called and whatever configuration sources it added
/// after it. The host here uses the <c>IServiceCollection</c> overload over a configuration whose
/// only source reloads from its backing store, as a JSON file or environment variables do: a
/// value written into the configuration is lost on that source's next reload.
/// </summary>
[TestFixture]
public class ServerSettingsLogLevelTests
{
    private const string Category = "Trax.Probe";

    private Bunit.TestContext _ctx = null!;
    private IConfigurationRoot _configuration = null!;

    [SetUp]
    public void SetUp()
    {
        _ctx = new Bunit.TestContext();
        _ctx.Services.AddRadzenComponents();
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;

        _configuration = new ConfigurationBuilder()
            .Add(
                new ReloadingSource(
                    new Dictionary<string, string?>
                    {
                        ["Logging:LogLevel:Default"] = "Warning",
                        [$"Logging:LogLevel:{Category}"] = "Warning",
                    }
                )
            )
            .Build();

        var services = _ctx.Services;
        services.AddSingleton<IConfiguration>(_configuration);
        services.AddLogging(logging =>
        {
            logging.AddConfiguration(_configuration.GetSection("Logging"));
            logging.AddProvider(new NullProviderThatCounts());
        });
        services.AddSingleton<TraxMarker>();
        services.AddSingleton<IWebHostEnvironment>(new TestEnvironment());
        services.AddTraxDashboard();
        // AddTraxDashboard brings the server's own JS runtime; the test renderer needs bUnit's.
        services.AddSingleton<IJSRuntime>(_ctx.JSInterop.JSRuntime);

        var data = new InMemoryDataContextFactory();
        var config = new SchedulerConfiguration();
        var discovery = new TrainDiscoveryService(new ServiceCollection());
        services.AddSingleton(config);
        services.AddSingleton<IDataContextProviderFactory>(data);
        services.AddScoped<ITrainExecutionService>(sp => new TrainExecutionService(
            discovery,
            runExecutor: null!,
            concurrencyLimiter: null!,
            data,
            new MediatorConfiguration(),
            sp
        ));
        services.AddScoped<IOperationsService>(sp => new OperationsService(
            discovery,
            data,
            config,
            sp.GetRequiredService<ITrainExecutionService>()
        ));
    }

    [TearDown]
    public void TearDown() => _ctx.Dispose();

    [Test]
    public async Task A_saved_level_is_the_level_the_loggers_use()
    {
        var page = _ctx.RenderComponent<ServerSettingsPage>();

        await ChooseLevel(page, Category, "Debug");
        await ClickButton(page, "Save");

        Logger(Category).IsEnabled(LogLevel.Debug).Should().BeTrue("the page saved Debug");
        Messages().Should().NotContain(m => m.Severity == NotificationSeverity.Error);
    }

    [Test]
    public async Task A_saved_level_survives_a_reload_of_the_hosts_configuration()
    {
        var page = _ctx.RenderComponent<ServerSettingsPage>();
        await ChooseLevel(page, Category, "Debug");
        await ClickButton(page, "Save");

        _configuration.Reload();

        Logger(Category)
            .IsEnabled(LogLevel.Debug)
            .Should()
            .BeTrue("a reloaded source does not take back a level saved from the dashboard");
    }

    [Test]
    public async Task A_level_the_host_overrides_after_the_dashboard_is_reported_as_not_applied()
    {
        _ctx.Services.PostConfigure<LoggerFilterOptions>(options =>
            options.Rules.Add(new LoggerFilterRule(null, Category, LogLevel.Error, null))
        );
        var page = _ctx.RenderComponent<ServerSettingsPage>();

        await ChooseLevel(page, Category, "Debug");
        await ClickButton(page, "Save");

        Messages()
            .Should()
            .Contain(
                m => m.Severity == NotificationSeverity.Error && m.Detail!.Contains(Category),
                "the page reads back the level in force and says when it is not the one saved"
            );
    }

    private ILogger Logger(string category) =>
        _ctx.Services.GetRequiredService<ILoggerFactory>().CreateLogger(category);

    private IEnumerable<NotificationMessage> Messages() =>
        _ctx.Services.GetRequiredService<NotificationService>().Messages;

    /// <summary>
    /// Picks a level in the Logging grid. Its level pickers are the dropdowns listing the log
    /// levels, one per row in the page's order: Default first, then by category.
    /// </summary>
    private static async Task ChooseLevel(
        IRenderedComponent<ServerSettingsPage> page,
        string category,
        string level
    )
    {
        var pickers = page.FindComponents<RadzenDropDown<string>>()
            .Where(d => d.Instance.Data is IEnumerable<string> levels && levels.Contains("Debug"))
            .ToList();
        var categories = new[] { "Default", Category };
        pickers.Should().HaveCount(categories.Length, "one level picker per configured category");
        var picker = pickers[Array.IndexOf(categories, category)];
        await page.InvokeAsync(() => picker.Instance.ValueChanged.InvokeAsync(level));
    }

    private static async Task ClickButton(IRenderedComponent<ServerSettingsPage> page, string text)
    {
        var button = page.FindAll("button").Single(b => b.TextContent.Trim().EndsWith(text));
        await button.ClickAsync(new());
    }

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "Trax.Dashboard.Tests";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public string EnvironmentName { get; set; } = "Development";
    }

    /// <summary>A configuration source that reloads from a fixed store, like a JSON file.</summary>
    private sealed class ReloadingSource(IDictionary<string, string?> store) : IConfigurationSource
    {
        public IConfigurationProvider Build(IConfigurationBuilder builder) =>
            new ReloadingProvider(store);
    }

    private sealed class ReloadingProvider(IDictionary<string, string?> store)
        : ConfigurationProvider
    {
        public override void Load() =>
            Data = new Dictionary<string, string?>(store, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>A provider, so the logger factory has something to enable levels for.</summary>
    private sealed class NullProviderThatCounts : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new NullLogger();

        public void Dispose() { }

        private sealed class NullLogger : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter
            ) { }
        }
    }
}
