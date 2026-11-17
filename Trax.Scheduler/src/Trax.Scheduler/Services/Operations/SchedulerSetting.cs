using Trax.Core.Extensions;
using Trax.Effect.Models.SchedulerConfig;
using Trax.Scheduler.Configuration;

namespace Trax.Scheduler.Services.Operations;

/// <summary>A setting of type <typeparamref name="T"/>, built from delegates.</summary>
internal sealed class SchedulerSetting<T>(
    string name,
    Func<SchedulerConfig, (bool IsSet, T Value)>? readColumn,
    Action<SchedulerConfig, T>? writeColumn,
    Func<SchedulerSettingsTarget, T> readLive,
    Action<SchedulerSettingsTarget, T> writeLive,
    Func<UpdateSchedulerConfigInput, (bool IsSet, T Value)> readPatch,
    Func<T, string, string?>? check = null,
    Func<SchedulerSettingsTarget, bool>? appliesTo = null
) : ISchedulerSetting
{
    public string Name => name;

    public bool AppliesTo(SchedulerSettingsTarget target) => appliesTo?.Invoke(target) ?? true;

    public bool TryReadRow(SchedulerConfig row, out object? value)
    {
        if (row.Overrides is null)
            return TryReadLegacyRow(row, out value);

        var isSet = row.TryGetOverride<T>(name, out var stored);
        value = stored;
        return isSet;
    }

    public void WriteRow(SchedulerConfig row, object? value)
    {
        row.SetOverride(name, (T)value.NotNull());
        writeColumn?.Invoke(row, (T)value.NotNull());
    }

    public bool TryReadLegacyRow(SchedulerConfig row, out object? value)
    {
        if (readColumn is null)
        {
            value = null;
            return false;
        }

        var (isSet, v) = readColumn(row);
        value = v;
        return isSet;
    }

    public object? ReadLive(SchedulerSettingsTarget target) => readLive(target);

    public void WriteLive(SchedulerSettingsTarget target, object? value) =>
        writeLive(target, (T)value.NotNull());

    public string? Check(object? value) => check?.Invoke((T)value.NotNull(), name);

    public bool TryReadPatch(UpdateSchedulerConfigInput input, out object? value)
    {
        var (isSet, v) = readPatch(input);
        value = v;
        return isSet;
    }
}
