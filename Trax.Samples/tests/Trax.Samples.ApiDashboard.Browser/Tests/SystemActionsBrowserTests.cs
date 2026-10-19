using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Trax.Samples.ApiDashboard.Browser.Factories;
using Trax.Samples.ApiDashboard.Browser.Fixtures;
using Trax.Scheduler.Services.Effects;
using Trax.Scheduler.Services.LogLevels;

namespace Trax.Samples.ApiDashboard.Browser.Tests;

/// <summary>
/// The System area's in-process settings: an effect switched off and saved, an effect's setting written through
/// Configure (and one that does not read as its type refused), and a log category's level changed. Each is read
/// back from the host's own services and put back afterwards, since every test shares the host.
/// </summary>
[TestFixture]
public class SystemActionsBrowserTests : ConsoleTest
{
    private static readonly float Render = BrowserTimeouts.Render.Ms();

    [Test]
    [Covers("route:/system/effects")]
    [Covers("mutation:SetEffectEnabled")]
    public async Task An_effect_switched_off_and_saved_stops_running_in_this_process()
    {
        var effect = Effects()
            .Where(e => e.Toggleable && e.Enabled)
            .OrderBy(e => e.FullName.Contains("Progress") ? 0 : 1)
            .First();
        try
        {
            await Console.OpenAsync("system/effects", Caller.Operator);
            await Assertions
                .Expect(Console.Heading("Effects"))
                .ToBeVisibleAsync(new() { Timeout = Render });
            var toggle = Page.GetByRole(
                AriaRole.Switch,
                new() { Name = $"{effect.Name} enabled", Exact = true }
            );
            await Assertions.Expect(toggle).ToBeCheckedAsync(new() { Timeout = Render });

            await toggle.ClickAsync();
            await Assertions.Expect(Page.GetByText("1 unsaved change(s)")).ToBeVisibleAsync();
            var sent = Console.SendsAsync("SetEffectEnabled");
            await Console.Button("Save").ClickAsync();
            await sent;

            await Assertions
                .Expect(Console.Toast("Effect settings updated."))
                .ToBeVisibleAsync(new() { Timeout = Render });
            await Assertions.Expect(toggle).Not.ToBeCheckedAsync(new() { Timeout = Render });
            Effects().Single(e => e.FullName == effect.FullName).Enabled.Should().BeFalse();
        }
        finally
        {
            Settings(s => s.SetEffectEnabled(effect.FullName, true));
        }
    }

    [Test]
    [Covers("route:/system/effects")]
    [Covers("mutation:ConfigureEffect")]
    public async Task Configure_writes_an_effects_setting()
    {
        var (effect, field) = BooleanSetting();
        var label = Label(field.Name);
        try
        {
            await OpenConfigureAsync(effect);
            var dialog = Console.Dialog($"Configure {effect.Name}");
            var setting = dialog.GetByRole(AriaRole.Switch, new() { Name = label, Exact = true });
            if (field.Value == "true")
                await Assertions.Expect(setting).ToBeCheckedAsync(new() { Timeout = Render });
            else
                await Assertions.Expect(setting).Not.ToBeCheckedAsync(new() { Timeout = Render });

            await setting.ClickAsync();
            var sent = Console.SendsAsync("ConfigureEffect");
            await dialog
                .GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true })
                .ClickAsync();
            await sent;

            await Assertions.Expect(dialog).ToHaveCountAsync(0, new() { Timeout = Render });
            Field(effect.FullName, field.Name)
                .Value.Should()
                .Be(field.Value == "true" ? "false" : "true", "the switch was flipped and saved");
        }
        finally
        {
            Settings(s =>
                s.ConfigureEffect(
                    effect.FullName,
                    new Dictionary<string, string?> { [field.Name] = field.Value }
                )
            );
        }
    }

    [Test]
    [Covers("route:/system/effects")]
    [Covers("mutation:ConfigureEffect")]
    public async Task Configure_refuses_a_setting_that_does_not_read_as_its_type_and_keeps_the_dialog_open()
    {
        var effect = Effects()
            .First(e =>
                e.Fields.Any(f =>
                    f.Kind == EffectFieldKind.Text && f.TypeName is "Int32" or "Int64"
                )
            );
        var field = effect.Fields.First(f =>
            f.Kind == EffectFieldKind.Text && f.TypeName is "Int32" or "Int64"
        );
        await OpenConfigureAsync(effect);
        var dialog = Console.Dialog($"Configure {effect.Name}");

        await dialog.GetByLabel(Label(field.Name), new() { Exact = true }).FillAsync("lots");
        var sent = Console.SendsAsync("ConfigureEffect");
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await sent;

        await Assertions
            .Expect(dialog.GetByRole(AriaRole.Alert))
            .ToBeVisibleAsync(new() { Timeout = Render });
        await Assertions
            .Expect(dialog.GetByTestId($"setting-error-{field.Name}"))
            .ToBeVisibleAsync(new() { Timeout = Render });
        Field(effect.FullName, field.Name).Value.Should().Be(field.Value, "nothing was written");
    }

    [Test]
    [Covers("route:/system/scheduler")]
    [Covers("mutation:SetLogLevels")]
    public async Task A_log_category_set_to_Error_is_filtered_at_Error_in_this_process()
    {
        try
        {
            await Console.OpenAsync("system/scheduler", Caller.Operator);
            var level = Page.GetByLabel(
                $"{ConsoleHost.LoggedCategory} level",
                new() { Exact = true }
            );
            await Assertions.Expect(level).ToHaveValueAsync("Warning", new() { Timeout = Render });

            await level.SelectOptionAsync(new SelectOptionValue { Label = "Error" });
            var sent = Console.SendsAsync("SetLogLevels");
            await Console.Button("Save log levels").ClickAsync();
            var levels = (await sent)
                .PostDataJSON()!
                .Value.GetProperty("variables")
                .GetProperty("levels");
            levels.GetArrayLength().Should().Be(1, "only the changed category is sent");

            await Assertions
                .Expect(Console.Toast("log level(s) set"))
                .ToBeVisibleAsync(new() { Timeout = Render });
            Levels()
                .GetLogLevels()
                .Single(l => l.Category == ConsoleHost.LoggedCategory)
                .Level.Should()
                .Be(LogLevel.Error);
        }
        finally
        {
            Levels()
                .SetLogLevels([new LogLevelChange(ConsoleHost.LoggedCategory, LogLevel.Warning)]);
        }
    }

    private async Task OpenConfigureAsync(EffectSettings effect)
    {
        await Console.OpenAsync("system/effects", Caller.Operator);
        await Page.GetByRole(
                AriaRole.Button,
                new() { Name = $"Configure {effect.Name}", Exact = true }
            )
            .ClickAsync(new() { Timeout = Render });
    }

    private static (EffectSettings Effect, EffectSettingField Field) BooleanSetting()
    {
        var effect = Effects()
            .First(e => e.Fields.Any(f => f.Kind == EffectFieldKind.Boolean && !f.Sensitive));
        return (
            effect,
            effect.Fields.First(f => f.Kind == EffectFieldKind.Boolean && !f.Sensitive)
        );
    }

    private static string Label(string name) =>
        System.Text.RegularExpressions.Regex.Replace(name, "(?<=[a-z0-9])(?=[A-Z])", " ");

    private static EffectSettingField Field(string effect, string name) =>
        Effects().Single(e => e.FullName == effect).Fields.Single(f => f.Name == name);

    private static IReadOnlyList<EffectSettings> Effects() => Settings(s => s.GetEffects());

    // Scoped on the host, so each read or write takes a scope of its own.
    private static T Settings<T>(Func<IEffectSettingsService, T> use)
    {
        using var scope = Host.Services.CreateScope();
        return use(scope.ServiceProvider.GetRequiredService<IEffectSettingsService>());
    }

    private static ILogLevelService Levels() =>
        Host.Services.GetRequiredService<ILogLevelService>();
}
