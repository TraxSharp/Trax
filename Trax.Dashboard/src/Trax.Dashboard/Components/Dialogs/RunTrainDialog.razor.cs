using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Radzen;
using Trax.Effect.Configuration.TraxEffectConfiguration;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Services.BackgroundTaskServer;

namespace Trax.Dashboard.Components.Dialogs;

public partial class RunTrainDialog
{
    [Inject]
    private IDataContextProviderFactory DataContextFactory { get; set; } = default!;

    [Inject]
    private IBackgroundTaskServer BackgroundTaskServer { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private DialogService DialogService { get; set; } = default!;

    [Parameter]
    public required TrainRegistration Registration { get; set; }

    private int _selectedTab;
    private string _jsonInput = "";
    private string? _error;
    private bool _running;

    private PropertyInfo[] _inputProperties = [];
    private readonly Dictionary<string, object?> _formValues = new();

    protected override void OnInitialized()
    {
        _inputProperties = Registration
            .InputType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead)
            .ToArray();

        foreach (var prop in _inputProperties)
        {
            var underlying = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;

            if (underlying == typeof(bool))
                _formValues[prop.Name] = false;
            else if (underlying.IsEnum)
                _formValues[prop.Name] = Enum.GetNames(underlying).FirstOrDefault() ?? "";
            else
                _formValues[prop.Name] = "";
        }
    }

    private T GetFormValue<T>(string name) =>
        _formValues.TryGetValue(name, out var value) && value is T typed ? typed : default!;

    private void SetFormValue(string name, object? value) => _formValues[name] = value;

    private async Task EnqueueTrain()
    {
        _error = null;
        _running = true;

        try
        {
            var input =
                _selectedTab == 0
                    ? BuildInputFromForm()
                    : JsonSerializer.Deserialize(
                        _jsonInput,
                        Registration.InputType,
                        TraxEffectConfiguration.StaticSystemJsonSerializerOptions
                    );

            if (input is null)
            {
                _error =
                    $"Deserialization returned null. Ensure the input matches {Registration.InputTypeName}.";
                return;
            }

            var metadata = Metadata.Create(
                new CreateMetadata
                {
                    Name = Registration.ServiceType.FullName!,
                    ExternalId = Guid.NewGuid().ToString("N"),
                    Input = null,
                }
            );

            using var dataContext = await DataContextFactory.CreateDbContextAsync(
                CancellationToken.None
            );
            await dataContext.Track(metadata);
            await dataContext.SaveChanges(CancellationToken.None);

            await BackgroundTaskServer.EnqueueAsync(metadata.Id, input);

            DialogService.Close();
            Navigation.NavigateTo($"trax/data/metadata/{metadata.Id}");
        }
        catch (JsonException je)
        {
            _error = $"Invalid JSON: {je.Message}";
        }
        catch (Exception ex)
        {
            _error = ex.Message;
        }
        finally
        {
            _running = false;
        }
    }

    private object? BuildInputFromForm()
    {
        var jsonObj = new JsonObject();

        foreach (var prop in _inputProperties)
        {
            var value = _formValues.GetValueOrDefault(prop.Name);
            jsonObj[prop.Name] = ToJsonNode(value, prop.PropertyType);
        }

        return JsonSerializer.Deserialize(
            jsonObj.ToJsonString(),
            Registration.InputType,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
        );
    }

    private static JsonNode? ToJsonNode(object? value, Type targetType)
    {
        var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (value is bool b)
            return JsonValue.Create(b);

        if (value is not string s || string.IsNullOrEmpty(s))
            return Nullable.GetUnderlyingType(targetType) is not null
                ? null
                : ToDefault(underlying);

        if (underlying == typeof(string))
            return JsonValue.Create(s);
        if (underlying.IsEnum)
            return JsonValue.Create(s);
        if (underlying == typeof(int) && int.TryParse(s, out var i))
            return JsonValue.Create(i);
        if (underlying == typeof(long) && long.TryParse(s, out var l))
            return JsonValue.Create(l);
        if (underlying == typeof(double) && double.TryParse(s, out var d))
            return JsonValue.Create(d);
        if (underlying == typeof(decimal) && decimal.TryParse(s, out var dec))
            return JsonValue.Create(dec);
        if (underlying == typeof(float) && float.TryParse(s, out var f))
            return JsonValue.Create(f);
        if (underlying == typeof(short) && short.TryParse(s, out var sh))
            return JsonValue.Create(sh);
        if (underlying == typeof(byte) && byte.TryParse(s, out var by))
            return JsonValue.Create(by);
        if (underlying == typeof(Guid) && Guid.TryParse(s, out var g))
            return JsonValue.Create(g);
        if (underlying == typeof(DateTime) && DateTime.TryParse(s, out var dt))
            return JsonValue.Create(dt);
        if (underlying == typeof(DateTimeOffset) && DateTimeOffset.TryParse(s, out var dto))
            return JsonValue.Create(dto);
        if (underlying == typeof(bool) && bool.TryParse(s, out var bo))
            return JsonValue.Create(bo);

        // Complex types: try parsing as JSON, fall back to string
        try
        {
            return JsonNode.Parse(s);
        }
        catch
        {
            return JsonValue.Create(s);
        }
    }

    private static JsonNode? ToDefault(Type type)
    {
        if (type == typeof(string))
            return JsonValue.Create("");
        if (type == typeof(bool))
            return JsonValue.Create(false);
        if (type.IsValueType)
            return JsonValue.Create(0);
        return null;
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
            _ when type == typeof(DateTime) || type == typeof(DateTimeOffset) =>
                "yyyy-MM-dd HH:mm:ss",
            _ => $"Enter {type.Name}",
        };
}
