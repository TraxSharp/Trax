using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Radzen;

namespace Trax.Dashboard.Components.Dialogs;

public partial class ConfigureEffectDialog
{
    [Inject]
    private DialogService DialogService { get; set; } = default!;

    [Inject]
    private NotificationService NotificationService { get; set; } = default!;

    [Parameter]
    public required Type ConfigurationType { get; set; }

    [Parameter]
    public required object Configuration { get; set; }

    private PropertyInfo[] _configProperties = [];
    private readonly Dictionary<string, object?> _formValues = new();
    private Dictionary<string, object?> _originalValues = new();
    private string? _error;

    protected override void OnInitialized()
    {
        _configProperties = ConfigurationType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite)
            .ToArray();

        foreach (var prop in _configProperties)
        {
            var currentValue = prop.GetValue(Configuration);
            var underlying = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;

            if (underlying == typeof(bool))
            {
                _formValues[prop.Name] = currentValue is bool b && b;
            }
            else if (underlying.IsEnum)
            {
                _formValues[prop.Name] = currentValue?.ToString() ?? "";
            }
            else
            {
                _formValues[prop.Name] = currentValue?.ToString() ?? "";
            }

            _originalValues[prop.Name] = currentValue;
        }
    }

    private T GetFormValue<T>(string name) =>
        _formValues.TryGetValue(name, out var value) && value is T typed ? typed : default!;

    private void SetFormValue(string name, object? value) => _formValues[name] = value;

    private void Save()
    {
        _error = null;

        try
        {
            foreach (var prop in _configProperties)
            {
                var formValue = _formValues.GetValueOrDefault(prop.Name);
                var underlying = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;

                object? converted;
                if (underlying == typeof(bool))
                {
                    converted = formValue is bool b && b;
                }
                else if (underlying.IsEnum)
                {
                    converted =
                        formValue is string s && !string.IsNullOrEmpty(s)
                            ? Enum.Parse(underlying, s)
                            : prop.GetValue(Configuration);
                }
                else
                {
                    converted = ConvertValue(formValue?.ToString(), underlying);
                }

                prop.SetValue(Configuration, converted);
            }

            NotificationService.Notify(
                new NotificationMessage
                {
                    Severity = NotificationSeverity.Success,
                    Summary = "Configuration Saved",
                    Detail =
                        $"{ConfigurationType.Name} updated. Changes apply to the next workflow execution.",
                    Duration = 4000,
                }
            );

            DialogService.Close();
        }
        catch (Exception ex)
        {
            _error = $"Failed to save configuration: {ex.Message}";
        }
    }

    private void Cancel()
    {
        // Restore original values on cancel
        foreach (var prop in _configProperties)
        {
            if (_originalValues.TryGetValue(prop.Name, out var original))
                prop.SetValue(Configuration, original);
        }

        DialogService.Close();
    }

    private static object? ConvertValue(string? value, Type targetType)
    {
        if (string.IsNullOrEmpty(value))
            return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;

        if (targetType == typeof(string))
            return value;
        if (targetType == typeof(int) && int.TryParse(value, out var i))
            return i;
        if (targetType == typeof(long) && long.TryParse(value, out var l))
            return l;
        if (targetType == typeof(double) && double.TryParse(value, out var d))
            return d;
        if (targetType == typeof(decimal) && decimal.TryParse(value, out var dec))
            return dec;
        if (targetType == typeof(float) && float.TryParse(value, out var f))
            return f;
        if (targetType == typeof(Guid) && Guid.TryParse(value, out var g))
            return g;

        return Convert.ChangeType(value, targetType);
    }

    private static string FormatLabel(string name) =>
        Regex.Replace(name, @"(?<=[a-z0-9])(?=[A-Z])", " ");

    private static string GetPlaceholder(Type type) =>
        type switch
        {
            _ when type == typeof(string) => "Enter text",
            _ when type == typeof(int) || type == typeof(long) || type == typeof(short) =>
                "Enter number",
            _ when type == typeof(double) || type == typeof(float) || type == typeof(decimal) =>
                "Enter decimal",
            _ when type == typeof(Guid) => "Enter GUID",
            _ => $"Enter {type.Name}",
        };
}
