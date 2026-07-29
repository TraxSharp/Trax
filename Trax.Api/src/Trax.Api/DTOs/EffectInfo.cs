namespace Trax.Api.DTOs;

/// <summary>
/// A registered observational effect and its runtime state, as seen by THIS process. Read-only by
/// design: the effect registry is an in-memory per-process singleton with no persistence or
/// cross-process broadcast, so a toggle from the API host would not reach the scheduler/worker
/// processes where effects actually run. Backs the dashboard's effects list.
/// </summary>
public record EffectInfo(string Name, string FullName, bool Enabled, bool Toggleable);
