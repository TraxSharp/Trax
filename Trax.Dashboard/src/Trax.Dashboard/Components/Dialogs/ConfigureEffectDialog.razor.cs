using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Radzen;
using Trax.Dashboard.Utilities;

namespace Trax.Dashboard.Components.Dialogs;

/// <summary>
/// Dialog that edits an effect provider's configuration object in place, opened from the
/// Effects settings page for a configurable effect. Save converts every field first and applies
/// all or none of them to the live object, which is process-wide and read by the next train
/// that runs; nothing is persisted, so a restart restores the configured values. Cancel writes
/// nothing. Opened by the dashboard's own pages through Radzen's <c>DialogService</c>; not intended to be used directly.
/// </summary>
public partial class ConfigureEffectDialog
{
    [Inject]
    private DialogService DialogService { get; set; } = default!;

    [Inject]
    private NotificationService NotificationService { get; set; } = default!;

    /// <summary>
    /// The configuration's type. Its public, readable and writable instance properties become
    /// the form's fields.
    /// </summary>
    [Parameter]
    public required Type ConfigurationType { get; set; }

    /// <summary>
    /// The live configuration instance to edit, an instance of <see cref="ConfigurationType"/>.
    /// Save writes to it directly.
    /// </summary>
    [Parameter]
    public required object Configuration { get; set; }

    private PropertyInfo[] _configProperties = [];
    private PropertyInfo[] _readOnlyProperties = [];
    private readonly Dictionary<string, object?> _formValues = new();
    private readonly Dictionary<string, object?> _openedWith = new();
    private string? _error;

    /// <summary>
    /// Reads the current value of every editable property of <see cref="Configuration"/> into the
    /// form. A property is editable when it is a boolean, an enum or a scalar
    /// <see cref="FormValueParser"/> reads; any other property, a predicate delegate for example,
    /// is shown as set in code and never written, since its text form cannot be read back.
    /// </summary>
    protected override void OnInitialized()
    {
        var properties = ConfigurationType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0)
            .ToArray();

        _configProperties = properties.Where(IsEditable).ToArray();
        _readOnlyProperties = properties.Where(p => !IsEditable(p)).ToArray();

        foreach (var prop in _configProperties)
        {
            var currentValue = prop.GetValue(Configuration);
            var underlying = Underlying(prop);

            _formValues[prop.Name] =
                underlying == typeof(bool) ? currentValue is true
                : underlying.IsEnum ? currentValue?.ToString() ?? ""
                : FormValueParser.Format(currentValue);
        }

        foreach (var (name, value) in _formValues)
            _openedWith[name] = value;
    }

    private static Type Underlying(PropertyInfo prop) =>
        Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;

    private static bool IsEditable(PropertyInfo prop)
    {
        var underlying = Underlying(prop);
        return underlying.IsEnum || FormValueParser.IsScalar(underlying);
    }

    private T GetFormValue<T>(string name) =>
        _formValues.TryGetValue(name, out var value) && value is T typed ? typed : default!;

    private void SetFormValue(string name, object? value) => _formValues[name] = value;

    /// <summary>
    /// Applies the fields this dialog changed to the live configuration, all or nothing. The
    /// object is the effect's process-wide configuration, read by every train that runs next, so
    /// every changed field is converted and validated before any is written, and if a setter
    /// throws part way the fields already written are put back. A field left as it opened is not
    /// written, so a value saved from elsewhere while the dialog was open is not reverted.
    /// </summary>
    private void Save()
    {
        _error = null;

        var changed = _configProperties
            .Where(p => !Equals(_formValues.GetValueOrDefault(p.Name), _openedWith[p.Name]))
            .ToList();

        var converted = new List<(PropertyInfo Property, object? Value)>();
        var refused = new List<string>();
        foreach (var prop in changed)
        {
            if (TryConvert(prop, out var value, out var error))
                converted.Add((prop, value));
            else
                refused.Add($"{FormatLabel(prop.Name)}: {error}");
        }

        if (refused.Count > 0)
        {
            _error = $"Failed to save configuration. {string.Join(" ", refused)}";
            return;
        }

        var applied = new List<(PropertyInfo Property, object? Previous)>();
        try
        {
            foreach (var (prop, value) in converted)
            {
                var previous = prop.GetValue(Configuration);
                prop.SetValue(Configuration, value);
                applied.Add((prop, previous));
            }
        }
        catch (Exception ex)
        {
            for (var i = applied.Count - 1; i >= 0; i--)
                applied[i].Property.SetValue(Configuration, applied[i].Previous);

            _error = $"Failed to save configuration: {ex.InnerException?.Message ?? ex.Message}";
            return;
        }

        NotificationService.Notify(
            new NotificationMessage
            {
                Severity = NotificationSeverity.Success,
                Summary = "Configuration Saved",
                Detail =
                    $"{ConfigurationType.Name} updated. Changes apply to the next train execution.",
                Duration = 4000,
            }
        );

        DialogService.Close();
    }

    /// <summary>
    /// Reads a field as its property's type: blank is <see langword="null"/> for a property that
    /// accepts null and refused for one that does not, text is read by
    /// <see cref="FormValueParser"/>, and the result must pass the property's own
    /// <see cref="ValidationAttribute"/>s.
    /// </summary>
    private bool TryConvert(PropertyInfo prop, out object? value, out string? error)
    {
        var formValue = _formValues.GetValueOrDefault(prop.Name);
        var underlying = Underlying(prop);
        value = null;
        error = null;

        if (underlying == typeof(bool))
            value = formValue is true;
        else if (formValue is not string text || string.IsNullOrWhiteSpace(text))
        {
            if (underlying == typeof(string) && !FormValueParser.AcceptsNull(prop))
                value = "";
            else if (!FormValueParser.AcceptsNull(prop))
            {
                error = "A value is required.";
                return false;
            }
        }
        else if (!FormValueParser.TryParse(text, underlying, out value, out error))
            return false;

        foreach (var rule in prop.GetCustomAttributes<ValidationAttribute>(inherit: true))
        {
            var result = rule.GetValidationResult(
                value,
                new ValidationContext(Configuration) { MemberName = prop.Name }
            );
            if (result != ValidationResult.Success)
            {
                error = result?.ErrorMessage ?? $"{value} is not allowed.";
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Closes without writing. Nothing reaches the configuration except through Save, so there
    /// is nothing to put back, and writing the values the dialog opened with would undo a
    /// change saved from elsewhere in the meantime.
    /// </summary>
    private void Cancel() => DialogService.Close();

    private static string FormatLabel(string name) =>
        Regex.Replace(name, @"(?<=[a-z0-9])(?=[A-Z])", " ");
}
