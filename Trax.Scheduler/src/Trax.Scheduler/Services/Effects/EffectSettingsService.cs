using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Attributes;
using Trax.Effect.Services.EffectProviderFactory;
using Trax.Effect.Services.EffectRegistry;
using Trax.Effect.Utils;
using Trax.Scheduler.Services.Operations;

namespace Trax.Scheduler.Services.Effects;

/// <inheritdoc />
/// <param name="services">
/// The provider each effect factory is resolved from, and the effect registry when the host
/// registered one.
/// </param>
public sealed class EffectSettingsService(IServiceProvider services) : IEffectSettingsService
{
    /// <summary>
    /// Held while a configuration is written, so two writes to the same settings object cannot
    /// interleave and leave a mix of both, or put back each other's values on a rollback. The
    /// objects are process-wide, so the lock is too.
    /// </summary>
    private static readonly Lock WriteLock = new();

    private readonly IEffectRegistry? _registry = services.GetService<IEffectRegistry>();

    /// <inheritdoc />
    public bool IsAvailable => _registry is not null;

    /// <inheritdoc />
    public IReadOnlyList<EffectSettings> GetEffects()
    {
        if (_registry is null)
            return [];

        return _registry
            .GetAll()
            .Select(kvp =>
            {
                var configurable = services.GetService(kvp.Key) as IConfigurableProviderFactory;
                return new EffectSettings(
                    kvp.Key.Name,
                    FullNameOf(kvp.Key),
                    kvp.Value,
                    _registry.IsToggleable(kvp.Key),
                    IsConfigurable: configurable is not null,
                    ConfigurationTypeName: configurable?.GetConfigurationType().FullName,
                    Configuration: configurable is null ? null : SerializeSettings(configurable),
                    Fields: configurable is null ? [] : DescribeFields(configurable)
                );
            })
            .OrderBy(e => e.FullName, StringComparer.Ordinal)
            .ToList();
    }

    /// <inheritdoc />
    public OperationResult SetEffectEnabled(string fullName, bool enabled)
    {
        if (_registry is null)
            return new OperationResult(false, Message: NoRegistry);

        var factoryType = FindFactory(fullName);

        if (factoryType is null)
            return new OperationResult(
                false,
                Message: $"No effect named '{fullName}' is registered in this process."
            );

        if (!_registry.IsToggleable(factoryType))
            return new OperationResult(
                false,
                Message: $"The effect '{fullName}' is registered as not toggleable."
            );

        if (enabled)
            _registry.Enable(factoryType);
        else
            _registry.Disable(factoryType);

        return new OperationResult(
            true,
            Count: 1,
            Message: enabled ? "Effect enabled in this process" : "Effect disabled in this process"
        );
    }

    /// <inheritdoc />
    public EffectConfigurationResult ConfigureEffect(
        string fullName,
        IReadOnlyDictionary<string, string?> values
    )
    {
        if (_registry is null)
            return Refused(NoRegistry);

        var factoryType = FindFactory(fullName);
        if (factoryType is null)
            return Refused($"No effect named '{fullName}' is registered in this process.");

        if (services.GetService(factoryType) is not IConfigurableProviderFactory configurable)
            return Refused($"The effect '{fullName}' has no settings to configure.");

        if (values.Count == 0)
            return Refused("No settings were given.");

        var configuration = configurable.GetConfiguration();
        var properties = WritableProperties(configurable.GetConfigurationType())
            .ToDictionary(p => p.Name, StringComparer.Ordinal);

        // Every value is read and checked before any is written.
        var converted = new List<(PropertyInfo Property, object? Value)>();
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, text) in values)
        {
            if (!properties.TryGetValue(name, out var property))
                errors[name] = "No such setting.";
            else if (!IsEditable(property))
                errors[name] = "This setting is set in code and cannot be changed here.";
            else if (TryConvert(property, text, configuration, out var value, out var error))
                converted.Add((property, value));
            else
                errors[name] = error!;
        }

        if (errors.Count > 0)
            return new EffectConfigurationResult(
                false,
                0,
                "The configuration was not saved: "
                    + string.Join(" ", errors.Select(e => $"{e.Key}: {e.Value}")),
                errors
            );

        lock (WriteLock)
        {
            var applied = new List<(PropertyInfo Property, object? Previous)>();
            try
            {
                foreach (var (property, value) in converted)
                {
                    var previous = property.GetValue(configuration);
                    property.SetValue(configuration, value);
                    applied.Add((property, previous));
                }
            }
            catch (Exception ex)
            {
                // All or none: the object is read by the next run in this process.
                for (var i = applied.Count - 1; i >= 0; i--)
                    applied[i].Property.SetValue(configuration, applied[i].Previous);

                var failed = converted[applied.Count].Property.Name;
                var reason =
                    (ex as TargetInvocationException)?.InnerException?.Message ?? ex.Message;
                return new EffectConfigurationResult(
                    false,
                    0,
                    $"The configuration was not saved: {failed}: {reason}",
                    new Dictionary<string, string>(StringComparer.Ordinal) { [failed] = reason }
                );
            }
        }

        return new EffectConfigurationResult(
            true,
            converted.Count,
            $"{configurable.GetConfigurationType().Name} updated in this process. Changes apply to "
                + "the next train execution.",
            new Dictionary<string, string>(StringComparer.Ordinal)
        );
    }

    private const string NoRegistry = "No effect registry is registered in this process.";

    private static EffectConfigurationResult Refused(string message) =>
        new(false, 0, message, new Dictionary<string, string>(StringComparer.Ordinal));

    private static string FullNameOf(Type factoryType) => factoryType.FullName ?? factoryType.Name;

    private Type? FindFactory(string fullName) =>
        _registry!
            .GetAll()
            .Keys.FirstOrDefault(t =>
                string.Equals(FullNameOf(t), fullName, StringComparison.Ordinal)
            );

    /// <summary>The public, readable and writable instance properties of a settings type.</summary>
    private static PropertyInfo[] WritableProperties(Type configurationType) =>
        configurationType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0)
            .ToArray();

    private static Type Underlying(PropertyInfo property) =>
        Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

    /// <summary>A boolean, an enum, or a scalar <see cref="SettingText"/> reads.</summary>
    private static bool IsEditable(PropertyInfo property)
    {
        var underlying = Underlying(property);
        return underlying.IsEnum || SettingText.IsScalar(underlying);
    }

    private static IReadOnlyList<EffectSettingField> DescribeFields(
        IConfigurableProviderFactory configurable
    )
    {
        var configuration = configurable.GetConfiguration();
        var properties = WritableProperties(configurable.GetConfigurationType());

        return properties
            .Where(IsEditable)
            .Concat(properties.Where(p => !IsEditable(p)))
            .Select(property => Describe(property, configuration))
            .ToList();
    }

    private static EffectSettingField Describe(PropertyInfo property, object configuration)
    {
        var underlying = Underlying(property);
        var sensitive = IsSensitive(property);
        var kind =
            !IsEditable(property) ? EffectFieldKind.SetInCode
            : underlying == typeof(bool) ? EffectFieldKind.Boolean
            : underlying.IsEnum ? EffectFieldKind.Enum
            : EffectFieldKind.Text;

        object? current;
        try
        {
            current = property.GetValue(configuration);
        }
        catch (TargetInvocationException)
        {
            // A getter that throws has no value to show, and does not cost the others theirs.
            current = null;
        }

        return new EffectSettingField(
            property.Name,
            underlying.Name,
            kind,
            Nullable: SettingText.AcceptsNull(property),
            EnumValues: underlying.IsEnum ? Enum.GetNames(underlying) : null,
            sensitive,
            HasValue: current is not null,
            Value: sensitive || kind == EffectFieldKind.SetInCode || current is null
                ? null
                : Format(current),
            Hint: kind switch
            {
                EffectFieldKind.Boolean => "true or false",
                EffectFieldKind.Enum => $"one of {string.Join(", ", Enum.GetNames(underlying))}",
                EffectFieldKind.SetInCode => "set in code",
                _ => SettingText.Placeholder(underlying),
            }
        );
    }

    /// <summary>A value as text, in the form <see cref="TryConvert"/> reads back.</summary>
    private static string Format(object value) =>
        value switch
        {
            bool b => b ? "true" : "false",
            Enum e => e.ToString(),
            _ => SettingText.Format(value),
        };

    /// <summary>
    /// Whether a settings property is marked <see cref="TraxSensitiveAttribute"/> where the
    /// masking of <see cref="TraxRedaction"/> finds a mark: on the property or a base declaration
    /// of it, on the record parameter of the same name, on an interface property it implements,
    /// or on its type.
    /// </summary>
    internal static bool IsSensitive(PropertyInfo property)
    {
        if (Attribute.IsDefined(property, typeof(TraxSensitiveAttribute), inherit: true))
            return true;

        var declaring = property.DeclaringType ?? property.ReflectedType;
        if (declaring is not null)
        {
            foreach (var constructor in declaring.GetConstructors())
            foreach (var parameter in constructor.GetParameters())
                if (
                    string.Equals(parameter.Name, property.Name, StringComparison.Ordinal)
                    && parameter.IsDefined(typeof(TraxSensitiveAttribute), inherit: false)
                )
                    return true;

            foreach (var contract in declaring.GetInterfaces())
                if (
                    contract.GetProperty(property.Name) is { } declared
                    && declared.IsDefined(typeof(TraxSensitiveAttribute), inherit: false)
                )
                    return true;
        }

        // Fails closed: a type marked sensitive marks the value it types.
        return Underlying(property).IsDefined(typeof(TraxSensitiveAttribute), inherit: true);
    }

    /// <summary>
    /// Reads a setting as its property's type: null or blank is null for a property that accepts
    /// null, the empty string for a non-nullable string, and refused otherwise; text is read by
    /// <see cref="SettingText"/>; and the result must pass the property's own
    /// <see cref="ValidationAttribute"/>s.
    /// </summary>
    private static bool TryConvert(
        PropertyInfo property,
        string? text,
        object configuration,
        out object? value,
        out string? error
    )
    {
        var underlying = Underlying(property);
        value = null;
        error = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            if (underlying == typeof(string) && !SettingText.AcceptsNull(property))
                value = "";
            else if (!SettingText.AcceptsNull(property))
            {
                error = "A value is required.";
                return false;
            }
        }
        else if (!SettingText.TryParse(text, underlying, out value, out error))
            return false;

        foreach (var rule in property.GetCustomAttributes<ValidationAttribute>(inherit: true))
        {
            var result = rule.GetValidationResult(
                value,
                new ValidationContext(configuration) { MemberName = property.Name }
            );
            if (result != ValidationResult.Success)
            {
                error = result?.ErrorMessage ?? $"{value} is not allowed.";
                return false;
            }
        }

        return true;
    }

    private static readonly JsonSerializerOptions SettingsJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        MaxDepth = 8,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary><see cref="SettingsJson"/> with every <c>[TraxSensitive]</c> member masked.</summary>
    private static readonly JsonSerializerOptions RedactedSettingsJson =
        TraxRedaction.WithRedaction(SettingsJson);

    // Serialized against the runtime type so a settings object typed as object still writes its
    // properties, and a [TraxSensitive] member at any depth is written as the mask. A settings type
    // System.Text.Json cannot write (a delegate, a pointer) reads as null rather than failing the
    // whole list.
    private static string? SerializeSettings(IConfigurableProviderFactory factory)
    {
        var settings = factory.GetConfiguration();
        try
        {
            return JsonSerializer.Serialize(settings, settings.GetType(), RedactedSettingsJson);
        }
        catch (Exception e) when (e is NotSupportedException or JsonException)
        {
            return null;
        }
    }
}
