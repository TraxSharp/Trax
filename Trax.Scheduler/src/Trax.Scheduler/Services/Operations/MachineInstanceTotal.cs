using Trax.Core.Exceptions;
using Trax.Effect.Enums;

namespace Trax.Scheduler.Services.Operations;

/// <summary>
/// How many instances match a <see cref="MachineInstanceQuery"/>, counted up to
/// <see cref="OperationsService.MachineInstanceCountCap"/>.
/// </summary>
/// <param name="Count">
/// The number of matching instances; when <paramref name="Capped"/> is set, the cap rather than
/// the number.
/// </param>
/// <param name="Capped">
/// True when more instances match than the cap, so the count is a lower bound. The counts by
/// state (<see cref="IOperationsService.GetMachineInstanceStateCountsAsync"/>) are never capped.
/// </param>
public record MachineInstanceTotal(int Count, bool Capped);
