using System.Collections.Concurrent;

namespace Trax.Effect.Services.EffectRegistry;

/// <summary>
/// Thread-safe implementation of IEffectRegistry using ConcurrentDictionary.
/// Registered as singleton in the DI container.
/// </summary>
public class EffectRegistry : IEffectRegistry
{
    private record EffectRegistration(bool Enabled, bool Toggleable);

    private readonly ConcurrentDictionary<Type, EffectRegistration> _effects = new();

    public bool IsEnabled(Type factoryType)
    {
        // Untracked types are always enabled (infrastructure effects)
        return !_effects.TryGetValue(factoryType, out var reg) || reg.Enabled;
    }

    public bool IsEnabled<TFactory>() => IsEnabled(typeof(TFactory));

    public void Enable(Type factoryType)
    {
        // Only allow toggling for toggleable effects
        if (_effects.TryGetValue(factoryType, out var reg) && reg.Toggleable)
            _effects[factoryType] = reg with { Enabled = true };
    }

    public void Enable<TFactory>() => Enable(typeof(TFactory));

    public void Disable(Type factoryType)
    {
        // Only allow toggling for toggleable effects
        if (_effects.TryGetValue(factoryType, out var reg) && reg.Toggleable)
            _effects[factoryType] = reg with { Enabled = false };
    }

    public void Disable<TFactory>() => Disable(typeof(TFactory));

    public bool IsToggleable(Type factoryType)
    {
        return _effects.TryGetValue(factoryType, out var reg) && reg.Toggleable;
    }

    public bool IsToggleable<TFactory>() => IsToggleable(typeof(TFactory));

    public IReadOnlyDictionary<Type, bool> GetAll()
    {
        return new Dictionary<Type, bool>(
            _effects.Select(kvp => new KeyValuePair<Type, bool>(kvp.Key, kvp.Value.Enabled))
        );
    }

    public IReadOnlyDictionary<Type, bool> GetToggleable()
    {
        return new Dictionary<Type, bool>(
            _effects
                .Where(kvp => kvp.Value.Toggleable)
                .Select(kvp => new KeyValuePair<Type, bool>(kvp.Key, kvp.Value.Enabled))
        );
    }

    public void Register(Type factoryType, bool enabled = true, bool toggleable = true)
    {
        _effects.TryAdd(factoryType, new EffectRegistration(enabled, toggleable));
    }
}
