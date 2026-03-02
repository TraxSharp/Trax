using Trax.Effect.Models.Manifest;

namespace Trax.Scheduler.Configuration;

/// <summary>
/// Describes a single manifest entry for use with the batch scheduling methods
/// (<c>ScheduleMany</c>, <c>IncludeMany</c>, <c>ThenIncludeMany</c>).
/// </summary>
/// <param name="Id">
/// The external ID suffix (for named overloads) or full external ID (for unnamed overloads).
/// </param>
/// <param name="Input">
/// The train input. Must match the <c>TInput</c> declared by the train's
/// <c>IEffectTrain&lt;TInput, Unit&gt;</c> interface.
/// </param>
/// <param name="DependsOn">
/// Optional external ID of the parent manifest this item depends on.
/// When <c>null</c>, <c>IncludeMany</c> falls back to the root scheduled manifest.
/// <c>ThenIncludeMany</c> requires this to be set.
/// </param>
public sealed record ManifestItem(string Id, IManifestProperties Input, string? DependsOn = null);
