using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Trax.Core.Extensions;
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
public partial class SystemActionsBrowserTests : ConsoleTest
{
    private static readonly float Render = BrowserTimeouts.Render.Ms();

    [Test]
    [Covers("route:/system/effects")]
    [Covers("mutation:SetEffectEnabled")]
    public async Task An_effect_switched_off_and_saved_stops_running_in_this_process()
    {
        var effect = (await EffectsAsync().ConfigureAwait(false))
            .Where(e => e.Toggleable && e.Enabled)
            .OrderBy(e => e.FullName.Contains("Progress", StringComparison.Ordinal) ? 0 : 1)
            .First();
        try
        {
            await Console.OpenAsync("system/effects", Caller.Operator).ConfigureAwait(false);
            await Assertions
                .Expect(Console.Heading("Effects"))
                .ToBeVisibleAsync(new() { Timeout = Render })
                .ConfigureAwait(false);
            var toggle = Page.GetByRole(
                AriaRole.Switch,
                new() { Name = $"{effect.Name} enabled", Exact = true }
            );
            await Assertions
                .Expect(toggle)
                .ToBeCheckedAsync(new() { Timeout = Render })
                .ConfigureAwait(false);

            await toggle.ClickAsync().ConfigureAwait(false);
            await Assertions
                .Expect(Page.GetByText("1 unsaved change(s)"))
                .ToBeVisibleAsync()
                .ConfigureAwait(false);
            var sent = Console.SendsAsync("SetEffectEnabled");
            await Console.Button("Save").ClickAsync().ConfigureAwait(false);
            await sent.ConfigureAwait(false);

            await Assertions
                .Expect(Console.Toast("Effect settings updated."))
                .ToBeVisibleAsync(new() { Timeout = Render })
                .ConfigureAwait(false);
            await Assertions
                .Expect(toggle)
                .Not.ToBeCheckedAsync(new() { Timeout = Render })
                .ConfigureAwait(false);
            (await EffectsAsync().ConfigureAwait(false))
                .Single(e => string.Equals(e.FullName, effect.FullName, StringComparison.Ordinal))
                .Enabled.Should()
                .BeFalse();
        }
        finally
        {
            await SettingsAsync(s => s.SetEffectEnabled(effect.FullName, enabled: true))
                .ConfigureAwait(false);
        }
    }

    [Test]
    [Covers("route:/system/effects")]
    [Covers("mutation:ConfigureEffect")]
    public async Task Configure_writes_an_effects_setting()
    {
        var (effect, field) = await BooleanSettingAsync().ConfigureAwait(false);
        var label = Label(field.Name);
        try
        {
            await OpenConfigureAsync(effect).ConfigureAwait(false);
            var dialog = Console.Dialog($"Configure {effect.Name}");
            var setting = dialog.GetByRole(AriaRole.Switch, new() { Name = label, Exact = true });
            if (string.Equals(field.Value, "true", StringComparison.Ordinal))
                await Assertions
                    .Expect(setting)
                    .ToBeCheckedAsync(new() { Timeout = Render })
                    .ConfigureAwait(false);
            else
                await Assertions
                    .Expect(setting)
                    .Not.ToBeCheckedAsync(new() { Timeout = Render })
                    .ConfigureAwait(false);

            await setting.ClickAsync().ConfigureAwait(false);
            var sent = Console.SendsAsync("ConfigureEffect");
            await dialog
                .GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true })
                .ClickAsync()
                .ConfigureAwait(false);
            await sent.ConfigureAwait(false);

            await Assertions
                .Expect(dialog)
                .ToHaveCountAsync(0, new() { Timeout = Render })
                .ConfigureAwait(false);
            (await FieldAsync(effect.FullName, field.Name).ConfigureAwait(false))
                .Value.Should()
                .Be(
                    string.Equals(field.Value, "true", StringComparison.Ordinal) ? "false" : "true",
                    "the switch was flipped and saved"
                );
        }
        finally
        {
            await SettingsAsync(s =>
                    s.ConfigureEffect(
                        effect.FullName,
                        new Dictionary<string, string?>(StringComparer.Ordinal)
                        {
                            [field.Name] = field.Value,
                        }
                    )
                )
                .ConfigureAwait(false);
        }
    }

    [Test]
    [Covers("route:/system/effects")]
    [Covers("mutation:ConfigureEffect")]
    public async Task Configure_refuses_a_setting_that_does_not_read_as_its_type_and_keeps_the_dialog_open()
    {
        var effect = (await EffectsAsync().ConfigureAwait(false)).First(e =>
            e.Fields.Any(f => f.Kind == EffectFieldKind.Text && f.TypeName is "Int32" or "Int64")
        );
        var field = effect.Fields.First(f =>
            f.Kind == EffectFieldKind.Text && f.TypeName is "Int32" or "Int64"
        );
        await OpenConfigureAsync(effect).ConfigureAwait(false);
        var dialog = Console.Dialog($"Configure {effect.Name}");

        await dialog
            .GetByLabel(Label(field.Name), new() { Exact = true })
            .FillAsync("lots")
            .ConfigureAwait(false);
        var sent = Console.SendsAsync("ConfigureEffect");
        await dialog
            .GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true })
            .ClickAsync()
            .ConfigureAwait(false);
        await sent.ConfigureAwait(false);

        await Assertions
            .Expect(dialog.GetByRole(AriaRole.Alert))
            .ToBeVisibleAsync(new() { Timeout = Render })
            .ConfigureAwait(false);
        await Assertions
            .Expect(dialog.GetByTestId($"setting-error-{field.Name}"))
            .ToBeVisibleAsync(new() { Timeout = Render })
            .ConfigureAwait(false);
        (await FieldAsync(effect.FullName, field.Name).ConfigureAwait(false))
            .Value.Should()
            .Be(field.Value, "nothing was written");
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
                .PostDataJSON()
                .NotNull()
                .Value.GetProperty("variables")
                .GetProperty("levels");
            levels.GetArrayLength().Should().Be(1, "only the changed category is sent");

            await Assertions
                .Expect(Console.Toast("log level(s) set"))
                .ToBeVisibleAsync(new() { Timeout = Render });
            Levels()
                .GetLogLevels()
                .Single(l =>
                    string.Equals(l.Category, ConsoleHost.LoggedCategory, StringComparison.Ordinal)
                )
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
        await Console.OpenAsync("system/effects", Caller.Operator).ConfigureAwait(false);
        await Page.GetByRole(
                AriaRole.Button,
                new() { Name = $"Configure {effect.Name}", Exact = true }
            )
            .ClickAsync(new() { Timeout = Render })
            .ConfigureAwait(false);
    }

    private static async Task<(
        EffectSettings Effect,
        EffectSettingField Field
    )> BooleanSettingAsync()
    {
        var effect = (await EffectsAsync().ConfigureAwait(false)).First(e =>
            e.Fields.Any(f => f.Kind == EffectFieldKind.Boolean && !f.Sensitive)
        );
        return (
            effect,
            effect.Fields.First(f => f.Kind == EffectFieldKind.Boolean && !f.Sensitive)
        );
    }

    private static string Label(string name) => WordBoundary.Replace(name, " ");

    private static async Task<EffectSettingField> FieldAsync(string effect, string name) =>
        (await EffectsAsync().ConfigureAwait(false))
            .Single(e => string.Equals(e.FullName, effect, StringComparison.Ordinal))
            .Fields.Single(f => string.Equals(f.Name, name, StringComparison.Ordinal));

    private static async Task<IReadOnlyList<EffectSettings>> EffectsAsync() =>
        await SettingsAsync(s => s.GetEffects()).ConfigureAwait(false);

    // Scoped on the host, so each read or write takes a scope of its own.
    private static async Task<T> SettingsAsync<T>(Func<IEffectSettingsService, T> use)
    {
        var scope = Host.Services.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
            return use(scope.ServiceProvider.GetRequiredService<IEffectSettingsService>());
    }

    private static ILogLevelService Levels() =>
        Host.Services.GetRequiredService<ILogLevelService>();

    [GeneratedRegex("(?<=[a-z0-9])(?=[A-Z])", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex WordBoundary { get; }
}
