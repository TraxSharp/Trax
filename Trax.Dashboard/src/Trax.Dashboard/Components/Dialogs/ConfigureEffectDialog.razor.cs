using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Radzen;
using Trax.Scheduler.Services.Effects;

namespace Trax.Dashboard.Components.Dialogs;

/// <summary>
/// Dialog that edits an effect's settings, opened from the Effects settings page for a
/// configurable effect. It reads the settings and writes the ones the operator changed through
/// <see cref="IEffectSettingsService"/>, the service the API's effects query and mutations call,
/// so both surfaces refuse the same values and never show a sensitive setting. Save writes all
/// or none of the changed settings to the effect's process-wide settings object, read by the next
/// train that runs in this process; nothing is persisted, so a restart restores the configured
/// values. Cancel writes nothing. Opened by the dashboard's own pages through Radzen's
/// <c>DialogService</c>; not intended to be used directly.
/// </summary>
public partial class ConfigureEffectDialog
{
    [Inject]
    private DialogService DialogService { get; set; } = default!;

    [Inject]
    private NotificationService NotificationService { get; set; } = default!;

    [Inject]
    private IEffectSettingsService EffectSettings { get; set; } = default!;

    /// <summary>The effect factory's full type name, as <see cref="IEffectSettingsService"/> names it.</summary>
    [Parameter]
    public required string EffectFullName { get; set; }

    private EffectSettings? _effect;
    private EffectSettingField[] _editable = [];
    private EffectSettingField[] _setInCode = [];
    private readonly Dictionary<string, string?> _formValues = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string?> _openedWith = new(StringComparer.Ordinal);
    private string? _error;

    /// <summary>
    /// Reads the effect's settings into the form. A sensitive setting's value is never read back,
    /// so its field opens blank and is written only when the operator types a new value. A
    /// setting with no text form, a predicate delegate for example, is shown as set in code.
    /// </summary>
    protected override void OnInitialized()
    {
        _effect = EffectSettings
            .GetEffects()
            .FirstOrDefault(e => e.IsConfigurable && e.FullName == EffectFullName);
        if (_effect is null)
            return;

        _editable = _effect.Fields.Where(f => f.Kind != EffectFieldKind.SetInCode).ToArray();
        _setInCode = _effect.Fields.Where(f => f.Kind == EffectFieldKind.SetInCode).ToArray();

        foreach (var field in _editable)
            _formValues[field.Name] = _openedWith[field.Name] = field.Sensitive
                ? ""
                : field.Value ?? "";
    }

    private T GetFormValue<T>(string name) =>
        _formValues.TryGetValue(name, out var value) && value is T typed ? typed : default!;

    private void SetFormValue(string name, string? value) => _formValues[name] = value ?? "";

    /// <summary>
    /// Sends the settings this dialog changed, and only those, so a value saved from elsewhere
    /// while the dialog was open is not reverted. A sensitive setting left blank is not sent. The
    /// service writes all of them or none; a refusal is shown and the dialog stays open.
    /// </summary>
    private void Save()
    {
        _error = null;
        if (_effect is null)
            return;

        var changed = _editable
            .Where(f =>
                f.Sensitive
                    ? !string.IsNullOrEmpty(_formValues.GetValueOrDefault(f.Name))
                    : _formValues.GetValueOrDefault(f.Name) != _openedWith[f.Name]
            )
            .ToDictionary(f => f.Name, f => _formValues.GetValueOrDefault(f.Name));

        if (changed.Count == 0)
        {
            DialogService.Close();
            return;
        }

        var result = EffectSettings.ConfigureEffect(EffectFullName, changed);
        if (!result.Success)
        {
            _error = result.Message;
            return;
        }

        NotificationService.Notify(
            new NotificationMessage
            {
                Severity = NotificationSeverity.Success,
                Summary = "Configuration Saved",
                Detail = result.Message,
                Duration = 4000,
            }
        );

        DialogService.Close();
    }

    /// <summary>
    /// Closes without writing. Nothing reaches the settings except through Save, so there is
    /// nothing to put back, and writing the values the dialog opened with would undo a change
    /// saved from elsewhere in the meantime.
    /// </summary>
    private void Cancel() => DialogService.Close();

    private static string ShortTypeName(string? fullName) =>
        fullName is null ? "" : fullName[(fullName.LastIndexOfAny(['.', '+']) + 1)..];

    private static string FormatLabel(string name) =>
        Regex.Replace(name, @"(?<=[a-z0-9])(?=[A-Z])", " ");
}
