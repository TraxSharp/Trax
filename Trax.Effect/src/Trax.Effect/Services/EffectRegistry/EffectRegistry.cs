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

    /// <inheritdoc/>
    public bool IsEnabled(Type factoryType)
    {
        // Untracked types are always enabled (infrastructure effects)
        return !_effects.TryGetValue(factoryType, out var reg) || reg.Enabled;
    }

    /// <inheritdoc/>
    public bool IsEnabled<TFactory>() => IsEnabled(typeof(TFactory));

    /// <summary>
    /// Enables the effect for <paramref name="factoryType"/>. Does nothing when the type is untracked or was
    /// registered as not toggleable.
    /// </summary>
    /// <param name="factoryType">The effect provider factory type the effect was registered under.</param>
    public void Enable(Type factoryType)
    {
        // Only allow toggling for toggleable effects
        if (_effects.TryGetValue(factoryType, out var reg) && reg.Toggleable)
            _effects[factoryType] = reg with { Enabled = true };
    }

    /// <summary>
    /// Enables the effect for <typeparamref name="TFactory"/>; see <see cref="Enable(Type)"/>.
    /// </summary>
    /// <typeparam name="TFactory">The effect provider factory type the effect was registered under.</typeparam>
    public void Enable<TFactory>() => Enable(typeof(TFactory));

    /// <summary>
    /// Disables the effect for <paramref name="factoryType"/>, so runners skip it on the next train or junction.
    /// Does nothing when the type is untracked or was registered as not toggleable. The setting is in memory for
    /// this process only.
    /// </summary>
    /// <param name="factoryType">The effect provider factory type the effect was registered under.</param>
    public void Disable(Type factoryType)
    {
        // Only allow toggling for toggleable effects
        if (_effects.TryGetValue(factoryType, out var reg) && reg.Toggleable)
            _effects[factoryType] = reg with { Enabled = false };
    }

    /// <summary>
    /// Disables the effect for <typeparamref name="TFactory"/>; see <see cref="Disable(Type)"/>.
    /// </summary>
    /// <typeparam name="TFactory">The effect provider factory type the effect was registered under.</typeparam>
    public void Disable<TFactory>() => Disable(typeof(TFactory));

    /// <inheritdoc/>
    public bool IsToggleable(Type factoryType)
    {
        return _effects.TryGetValue(factoryType, out var reg) && reg.Toggleable;
    }

    /// <inheritdoc/>
    public bool IsToggleable<TFactory>() => IsToggleable(typeof(TFactory));

    /// <inheritdoc/>
    public IReadOnlyDictionary<Type, bool> GetAll()
    {
        return new Dictionary<Type, bool>(
            _effects.Select(kvp => new KeyValuePair<Type, bool>(kvp.Key, kvp.Value.Enabled))
        );
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<Type, bool> GetToggleable()
    {
        return new Dictionary<Type, bool>(
            _effects
                .Where(kvp => kvp.Value.Toggleable)
                .Select(kvp => new KeyValuePair<Type, bool>(kvp.Key, kvp.Value.Enabled))
        );
    }

    /// <summary>
    /// Starts tracking <paramref name="factoryType"/>. The first registration of a type wins; a later call for the
    /// same type is ignored, so its enabled and toggleable flags cannot be changed this way. Called by the
    /// <c>AddEffect</c>, <c>AddJunctionEffect</c> and <c>AddLifecycleHook</c> builder methods.
    /// </summary>
    /// <param name="factoryType">The effect provider factory type to track.</param>
    /// <param name="enabled">Whether the effect starts enabled.</param>
    /// <param name="toggleable">Whether <see cref="Enable(Type)"/> and <see cref="Disable(Type)"/> may change it.</param>
    public void Register(Type factoryType, bool enabled = true, bool toggleable = true)
    {
        _effects.TryAdd(factoryType, new EffectRegistration(enabled, toggleable));
    }
}
