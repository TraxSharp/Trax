using Trax.Core.Exceptions;
using Trax.Effect.Enums;

namespace Trax.Scheduler.Services.Operations;

/// <summary>A page of state-machine instances, newest first by when each was last written.</summary>
/// <param name="Items">The instances on this page.</param>
/// <param name="Skip">The offset used.</param>
/// <param name="Take">The page size used, after clamping.</param>
public record MachineInstancePage(IReadOnlyList<MachineInstanceRecord> Items, int Skip, int Take);
