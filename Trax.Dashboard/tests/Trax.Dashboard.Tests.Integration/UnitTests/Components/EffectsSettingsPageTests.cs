using AwesomeAssertions;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Radzen.Blazor;
using Trax.Dashboard.Components.Pages.Settings;
using Trax.Effect.Services.EffectRegistry;
using Trax.Scheduler.Services.Effects;

namespace Trax.Dashboard.Tests.Integration.UnitTests.Components;

/// <summary>
/// The Effects page applies the toggles the operator changed, and nothing else, through the
/// Scheduler's effect settings service the GraphQL <c>setEffectEnabled</c> mutation calls, so a
/// change another writer made after the page loaded (another operator, the mutation) survives an
/// unrelated save.
/// </summary>
[TestFixture]
public class EffectsSettingsPageTests
{
    private Bunit.TestContext _ctx = null!;
    private EffectRegistry _registry = null!;

    [SetUp]
    public void SetUp()
    {
        _ctx = new Bunit.TestContext();
        _ctx.Services.AddRadzenComponents();
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;

        _registry = new EffectRegistry();
        _registry.Register(typeof(AlphaEffectFactory));
        _registry.Register(typeof(BetaEffectFactory));
        _ctx.Services.AddSingleton<IEffectRegistry>(_registry);
        _ctx.Services.AddScoped<IEffectSettingsService>(sp => new EffectSettingsService(sp));
    }

    [TearDown]
    public void TearDown() => _ctx.Dispose();

    [Test]
    public async Task A_save_keeps_a_toggle_another_writer_changed_after_the_page_loaded()
    {
        var page = _ctx.RenderComponent<EffectsSettingsPage>();
        _registry.Disable(typeof(AlphaEffectFactory));

        await Toggle(page, nameof(BetaEffectFactory), false);
        await ClickButton(page, "Save");

        _registry
            .IsEnabled(typeof(BetaEffectFactory))
            .Should()
            .BeFalse("the operator turned it off");
        _registry
            .IsEnabled(typeof(AlphaEffectFactory))
            .Should()
            .BeFalse("the page did not touch it, so it must not turn it back on");
    }

    [Test]
    public async Task After_a_save_the_page_shows_the_registry()
    {
        var page = _ctx.RenderComponent<EffectsSettingsPage>();
        _registry.Disable(typeof(AlphaEffectFactory));

        await Toggle(page, nameof(BetaEffectFactory), false);
        await ClickButton(page, "Save");

        Switch(page, nameof(AlphaEffectFactory))
            .Instance.Value.Should()
            .BeFalse("the page re-read the registry after saving");
        page.FindAll(".cs-fieldset-dirty").Should().BeEmpty();
    }

    [Test]
    public async Task Discard_changes_shows_the_registry_and_applies_nothing()
    {
        var page = _ctx.RenderComponent<EffectsSettingsPage>();
        _registry.Disable(typeof(AlphaEffectFactory));

        await Toggle(page, nameof(BetaEffectFactory), false);
        await ClickButton(page, "Discard Changes");

        _registry.IsEnabled(typeof(BetaEffectFactory)).Should().BeTrue();
        Switch(page, nameof(BetaEffectFactory)).Instance.Value.Should().BeTrue();
        Switch(page, nameof(AlphaEffectFactory)).Instance.Value.Should().BeFalse();
    }

    /// <summary>
    /// The switch on the row for <paramref name="factory"/>. Rows are sorted by name, and each
    /// toggleable effect has one switch.
    /// </summary>
    private static IRenderedComponent<RadzenSwitch> Switch(
        IRenderedComponent<EffectsSettingsPage> page,
        string factory
    )
    {
        var names = new[] { nameof(AlphaEffectFactory), nameof(BetaEffectFactory) };
        var switches = page.FindComponents<RadzenSwitch>();
        switches.Should().HaveCount(names.Length);
        return switches[Array.IndexOf(names, factory)];
    }

    private static async Task Toggle(
        IRenderedComponent<EffectsSettingsPage> page,
        string factory,
        bool enabled
    )
    {
        var toggle = Switch(page, factory);
        await page.InvokeAsync(() => toggle.Instance.ValueChanged.InvokeAsync(enabled));
    }

    private static async Task ClickButton(IRenderedComponent<EffectsSettingsPage> page, string text)
    {
        var button = page.FindAll("button").Single(b => b.TextContent.Trim().EndsWith(text));
        await button.ClickAsync(new());
    }

    private sealed class AlphaEffectFactory;

    private sealed class BetaEffectFactory;
}
