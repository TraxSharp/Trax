using Trax.Effect.Models.SchedulerConfig;
using Trax.Scheduler.Configuration;

namespace Trax.Scheduler.Services.Operations;

/// <summary>
/// One persisted scheduler setting: how it is read from and written to the
/// <c>trax.scheduler_config</c> row, from and to the running host, from a patch, and the range it
/// must stay in. <see cref="SchedulerSettings.All"/> lists every one, and the operations service's
/// save, the startup load and the live refresh all work through that list, so adding a setting
/// means adding its patch and snapshot fields and one entry there. The row stores a setting in
/// its <c>overrides</c> object, so a new setting needs no column.
/// </summary>
internal interface ISchedulerSetting
{
    /// <summary>
    /// The setting's name, as the patch input and the snapshot spell it, and the key it is stored
    /// under in the row's <c>overrides</c>.
    /// </summary>
    string Name { get; }

    /// <summary>Whether this host has the setting to apply (local workers, metadata cleanup).</summary>
    bool AppliesTo(SchedulerSettingsTarget target);

    /// <summary>
    /// The stored value, or false when the row does not set it and the host's configured value
    /// stands. A row with <c>overrides</c> sets exactly the settings it names. A row written before
    /// that column existed (<c>overrides</c> null) sets every setting it has a column for, as every
    /// save used to write them all.
    /// </summary>
    bool TryReadRow(SchedulerConfig row, out object? value);

    /// <summary>
    /// Names the setting in the row's <c>overrides</c> with <paramref name="value"/>, and keeps its
    /// column, when it has one, in step for a host still reading the columns.
    /// </summary>
    void WriteRow(SchedulerConfig row, object? value);

    /// <summary>
    /// Reads the setting from a row written before <c>overrides</c> existed, from its column.
    /// False when it has no column, or the column leaves it unset.
    /// </summary>
    bool TryReadLegacyRow(SchedulerConfig row, out object? value);

    /// <summary>The value the host runs with now.</summary>
    object? ReadLive(SchedulerSettingsTarget target);

    /// <summary>Makes the host run with <paramref name="value"/>.</summary>
    void WriteLive(SchedulerSettingsTarget target, object? value);

    /// <summary>What is wrong with <paramref name="value"/>, or null when the scheduler can run with it.</summary>
    string? Check(object? value);

    /// <summary>The value a patch sets, or false when the patch leaves the setting alone.</summary>
    bool TryReadPatch(UpdateSchedulerConfigInput input, out object? value);
}
