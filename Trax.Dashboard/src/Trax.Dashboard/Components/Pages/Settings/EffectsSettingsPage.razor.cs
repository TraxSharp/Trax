using Trax.Dashboard.Components.Dialogs;
using Trax.Effect.Services.EffectProviderFactory;
using Trax.Effect.Services.EffectRegistry;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Radzen;

namespace Trax.Dashboard.Components.Pages.Settings;

public partial class EffectsSettingsPage
{
    [Inject]
    private IServiceProvider ServiceProvider { get; set; } = default!;

    [Inject]
    private NotificationService NotificationService { get; set; } = default!;

    [Inject]
    private DialogService DialogService { get; set; } = default!;

    // ── Effects state ──
    private IEffectRegistry? _effectRegistry;
    private bool _effectsAvailable;
    private List<EffectEntry> _effects = [];
    private Dictionary<Type, bool> _savedEffectStates = new();

    // ── Dirty tracking ──
    private bool IsEffectsDirty =>
        _effectsAvailable
        && _effects.Any(
            e => e.Toggleable && e.Enabled != _savedEffectStates.GetValueOrDefault(e.FactoryType)
        );

    protected override void OnInitialized()
    {
        _effectRegistry = ServiceProvider.GetService<IEffectRegistry>();
        _effectsAvailable = _effectRegistry is not null;

        if (_effectsAvailable)
        {
            LoadEffects();
            SnapshotEffectState();
        }
    }

    // ── Effect helpers ──

    private void LoadEffects()
    {
        _effects = _effectRegistry!
            .GetAll()
            .Select(kvp =>
            {
                var factory = ServiceProvider.GetService(kvp.Key);
                var isConfigurable = factory is IConfigurableProviderFactory;

                return new EffectEntry
                {
                    FactoryType = kvp.Key,
                    Name = kvp.Key.Name,
                    FullName = kvp.Key.FullName ?? kvp.Key.Name,
                    Enabled = kvp.Value,
                    Toggleable = _effectRegistry.IsToggleable(kvp.Key),
                    IsConfigurable = isConfigurable,
                    Factory = factory,
                };
            })
            .OrderBy(e => e.Name)
            .ToList();
    }

    private void EnableAllEffects()
    {
        foreach (var entry in _effects.Where(e => e.Toggleable))
            entry.Enabled = true;
    }

    private void DisableAllEffects()
    {
        foreach (var entry in _effects.Where(e => e.Toggleable))
            entry.Enabled = false;
    }

    private void Save()
    {
        if (_effectRegistry is null)
            return;

        foreach (var entry in _effects.Where(e => e.Toggleable))
        {
            if (entry.Enabled)
                _effectRegistry.Enable(entry.FactoryType);
            else
                _effectRegistry.Disable(entry.FactoryType);
        }

        SnapshotEffectState();

        NotificationService.Notify(
            new NotificationMessage
            {
                Severity = NotificationSeverity.Success,
                Summary = "Effects Saved",
                Detail = "Effect settings updated.",
                Duration = 4000,
            }
        );
    }

    private void ResetDefaults()
    {
        foreach (var entry in _effects.Where(e => e.Toggleable))
            entry.Enabled = _savedEffectStates.GetValueOrDefault(entry.FactoryType);

        NotificationService.Notify(
            new NotificationMessage
            {
                Severity = NotificationSeverity.Info,
                Summary = "Defaults Restored",
                Detail = "Effect settings have been reset to their saved values.",
                Duration = 4000,
            }
        );
    }

    private void SnapshotEffectState()
    {
        _savedEffectStates = _effects.ToDictionary(e => e.FactoryType, e => e.Enabled);
    }

    private async Task OpenConfigureDialog(EffectEntry entry)
    {
        if (entry.Factory is not IConfigurableProviderFactory configurable)
            return;

        await DialogService.OpenAsync<ConfigureEffectDialog>(
            $"Configure {entry.Name}",
            new Dictionary<string, object>
            {
                ["ConfigurationType"] = configurable.GetConfigurationType(),
                ["Configuration"] = configurable.GetConfiguration(),
            },
            new DialogOptions
            {
                Width = "600px",
                Resizable = true,
                Draggable = true,
            }
        );
    }

    // ── Inner types ──

    private class EffectEntry
    {
        public required Type FactoryType { get; init; }
        public required string Name { get; init; }
        public required string FullName { get; init; }
        public bool Enabled { get; set; }
        public required bool Toggleable { get; init; }
        public required bool IsConfigurable { get; init; }
        public object? Factory { get; init; }
    }
}
