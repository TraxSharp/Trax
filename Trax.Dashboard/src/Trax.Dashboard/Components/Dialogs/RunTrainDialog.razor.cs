using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Radzen;
using Trax.Effect.Configuration.TraxEffectConfiguration;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Services.JobSubmitter;

namespace Trax.Dashboard.Components.Dialogs;

/// <summary>
/// Dialog that runs a train immediately with input entered through a generated form or as raw
/// JSON, opened from the Trains page. It writes a pending metadata row and hands the input
/// straight to the job submitter, so no work queue entry is created, dispatch never sees the run
/// and the train's subject key is not consulted; the dialog warns about this. If the submitter
/// refuses the run, the row is marked failed. On success it navigates to the new run.
/// Opened by the dashboard's own pages through Radzen's <c>DialogService</c>; not intended to be used directly.
/// </summary>
public partial class RunTrainDialog : IDisposable
{
    private readonly CancellationTokenSource _cts = new();

    [Inject]
    private IDataContextProviderFactory DataContextFactory { get; set; } = default!;

    [Inject]
    private IJobSubmitter JobSubmitter { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private DialogService DialogService { get; set; } = default!;

    /// <summary>
    /// The train to run, from train discovery. Its input type drives the form, and its service
    /// type's FullName becomes the run's name.
    /// </summary>
    [Parameter]
    public required TrainRegistration Registration { get; set; }

    private int _selectedTab;
    private string _jsonInput = "";
    private string? _error;
    private bool _running;

    private PropertyInfo[] _inputProperties = [];
    private readonly Dictionary<string, object?> _formValues = new();

    /// <summary>
    /// Builds one form field per public readable property of the train's input type, starting
    /// booleans at <see langword="false"/>, enums at their first name and everything else empty.
    /// </summary>
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
                        InputOptions()
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

            using (var dataContext = await DataContextFactory.CreateDbContextAsync(_cts.Token))
            {
                await dataContext.Track(metadata);
                await dataContext.SaveChanges(_cts.Token);
            }

            try
            {
                await JobSubmitter.EnqueueAsync(metadata.Id, input, _cts.Token);
            }
            catch (Exception submitFailure)
            {
                await FailUnsubmittedRun(metadata.Id, submitFailure);
                throw;
            }

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

    /// <summary>
    /// The job submitter refused the run, so no job exists to move its row out of Pending. Fail
    /// it here with the submitter's exception, as the job dispatcher does when a submit fails,
    /// rather than leave it for the stale-pending reaper to fail later for the wrong reason.
    /// </summary>
    private async Task FailUnsubmittedRun(long metadataId, Exception submitFailure)
    {
        try
        {
            using var dataContext = await DataContextFactory.CreateDbContextAsync(
                CancellationToken.None
            );
            var metadata = await dataContext.Metadatas.FirstOrDefaultAsync(m => m.Id == metadataId);
            if (metadata is null || metadata.TrainState != TrainState.Pending)
                return;

            metadata.TrainState = TrainState.Failed;
            metadata.EndTime = DateTime.UtcNow;
            metadata.AddException(submitFailure);
            await dataContext.SaveChanges(CancellationToken.None);
        }
        catch (Exception)
        {
            // The dialog reports the submit failure itself; the reaper still fails the row.
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
            InputOptions()
        );
    }

    /// <summary>
    /// The options both tabs read an input with: the host's train parameter options, so an
    /// enum or any other converter it configures reads the same from either tab, made
    /// case-insensitive and refusing a property given twice. The form's keys are the C#
    /// property names, and a person typing JSON may write them that way too; under a camelCase
    /// policy either would otherwise match nothing and leave the property at its default
    /// without an error. Once case is ignored, <c>{"amount":1,"Amount":999}</c> names one
    /// property twice, which is refused rather than resolved to either value.
    /// Trax.Docs/adr/0023-caller-supplied-train-input-is-read-case-insensitively.md records
    /// both rules for every caller-supplied train input.
    ///
    /// <para>One copy is kept for as long as the host's options are the same instance, and
    /// rebuilt if they are replaced.</para>
    /// </summary>
    private static JsonSerializerOptions InputOptions()
    {
        var source = TraxEffectConfiguration.StaticSystemJsonSerializerOptions;
        var cached = _inputOptions;
        if (cached is not null && ReferenceEquals(cached.Source, source))
            return cached.Options;

        var options = new JsonSerializerOptions(source)
        {
            PropertyNameCaseInsensitive = true,
            AllowDuplicateProperties = false,
        };
        options.MakeReadOnly(populateMissingResolver: true);

        _inputOptions = new DerivedOptions(source, options);
        return options;
    }

    private static DerivedOptions? _inputOptions;

    private sealed record DerivedOptions(
        JsonSerializerOptions Source,
        JsonSerializerOptions Options
    );

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

    /// <summary>
    /// Cancels a run request still in flight when the dialog closes.
    /// </summary>
    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
