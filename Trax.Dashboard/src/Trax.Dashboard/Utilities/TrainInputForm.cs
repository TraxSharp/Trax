using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Trax.Effect.Configuration.TraxEffectConfiguration;

namespace Trax.Dashboard.Utilities;

/// <summary>
/// The Form tab the Run and Queue dialogs share: one field per public readable property of a
/// train's input type, turned into the input JSON both dialogs hand the operations service.
/// Text fields are read by <see cref="FormValueParser"/>; a field that does not read as its type,
/// or a required field left blank, is refused by name and nothing is sent. Infrastructure for the
/// dashboard's own dialogs; not intended to be called directly.
/// </summary>
internal sealed class TrainInputForm
{
    private readonly Dictionary<string, object?> _values = new();
    private readonly Dictionary<string, string> _errors = new();
    private readonly Dictionary<string, string> _jsonNames;

    /// <summary>
    /// Builds the form for <paramref name="inputType"/>, starting booleans at
    /// <see langword="false"/>, enums at their first name and everything else empty.
    /// </summary>
    public TrainInputForm(Type inputType)
    {
        Properties = inputType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead)
            .ToArray();
        _jsonNames = JsonPropertyNames(inputType);

        foreach (var prop in Properties)
        {
            var underlying = Underlying(prop);

            if (underlying == typeof(bool))
                _values[prop.Name] = false;
            else if (underlying.IsEnum)
                _values[prop.Name] = Enum.GetNames(underlying).FirstOrDefault() ?? "";
            else
                _values[prop.Name] = "";
        }
    }

    /// <summary>The input type's properties, one field each.</summary>
    public PropertyInfo[] Properties { get; }

    /// <summary>
    /// Why each refused field was refused, by property name, from the last
    /// <see cref="TryBuildJson"/>.
    /// </summary>
    public IReadOnlyDictionary<string, string> Errors => _errors;

    /// <summary>The field's type with any <see cref="Nullable{T}"/> removed.</summary>
    public static Type Underlying(PropertyInfo prop) =>
        Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;

    /// <summary>The value a field holds, or the type's default when it holds another type.</summary>
    public T Get<T>(string name) =>
        _values.TryGetValue(name, out var value) && value is T typed ? typed : default!;

    /// <summary>Sets a field's value, clearing any error shown for it.</summary>
    public void Set(string name, object? value)
    {
        _values[name] = value;
        _errors.Remove(name);
    }

    /// <summary>
    /// Builds the input JSON from the form. Each key is the name the host's train parameter
    /// options read the property by, so <c>[JsonPropertyName]</c> is honoured.
    /// </summary>
    /// <param name="json">The input JSON, when every field was accepted.</param>
    /// <returns>
    /// <see langword="false"/> when a field was refused; <see cref="Errors"/> then says why.
    /// </returns>
    public bool TryBuildJson(out string json)
    {
        _errors.Clear();
        var jsonObj = new JsonObject();

        foreach (var prop in Properties)
        {
            if (
                TryToJsonNode(
                    prop,
                    _values.GetValueOrDefault(prop.Name),
                    out var node,
                    out var error
                )
            )
                jsonObj[_jsonNames.GetValueOrDefault(prop.Name, prop.Name)] = node;
            else
                _errors[prop.Name] = error!;
        }

        json = _errors.Count == 0 ? jsonObj.ToJsonString() : "";
        return _errors.Count == 0;
    }

    /// <summary>A line naming every refused field and why, for the dialog's error alert.</summary>
    public string ErrorSummary() =>
        string.Join(" ", _errors.Select(e => $"{FormatLabel(e.Key)}: {e.Value}"));

    /// <summary>Splits a PascalCase property name into words for its label.</summary>
    public static string FormatLabel(string name) =>
        System.Text.RegularExpressions.Regex.Replace(name, @"(?<=[a-z0-9])(?=[A-Z])", " ");

    private static bool TryToJsonNode(
        PropertyInfo prop,
        object? value,
        out JsonNode? node,
        out string? error
    )
    {
        var underlying = Underlying(prop);
        node = null;
        error = null;

        if (value is bool b)
        {
            node = JsonValue.Create(b);
            return true;
        }

        var text = value as string;
        if (string.IsNullOrWhiteSpace(text))
        {
            if (FormValueParser.AcceptsNull(prop))
                return true;
            if (underlying == typeof(string))
            {
                node = JsonValue.Create(text ?? "");
                return true;
            }

            error = "A value is required.";
            return false;
        }

        if (underlying.IsEnum || FormValueParser.IsScalar(underlying))
        {
            if (!FormValueParser.TryParse(text, underlying, out var parsed, out error))
                return false;

            node = underlying.IsEnum
                ? JsonValue.Create(text.Trim())
                : JsonSerializer.SerializeToNode(parsed, underlying);
            return true;
        }

        // Anything else is a structured value, which its field takes as JSON.
        try
        {
            node = JsonNode.Parse(text);
            return true;
        }
        catch (JsonException ex)
        {
            error = $"Not valid JSON for {underlying.Name}: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// The name each input property is read by, keyed by its C# name. The operations service
    /// reads the input with the host's train parameter options; asking those options' contract
    /// for the names also honours <c>[JsonPropertyName]</c>, which the naming policy alone would not.
    /// </summary>
    private static Dictionary<string, string> JsonPropertyNames(Type inputType)
    {
        var options = new JsonSerializerOptions(
            TraxEffectConfiguration.StaticSystemJsonSerializerOptions
        );
        options.MakeReadOnly(populateMissingResolver: true);

        return options
            .GetTypeInfo(inputType)
            .Properties.Where(p => p.AttributeProvider is MemberInfo)
            .GroupBy(p => ((MemberInfo)p.AttributeProvider!).Name)
            .ToDictionary(g => g.Key, g => g.First().Name);
    }
}
