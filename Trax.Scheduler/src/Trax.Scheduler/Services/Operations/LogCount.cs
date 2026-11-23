using Microsoft.Extensions.Logging;

namespace Trax.Scheduler.Services.Operations;

/// <summary>
/// How many log entries match a query, as <see cref="IOperationsService.CountLogsCappedAsync"/>
/// counts them.
/// </summary>
/// <param name="Count">
/// The number of matching entries; when <paramref name="Capped"/> is set, the cap
/// (<see cref="OperationsService.LogCountCap"/>) rather than the number.
/// </param>
/// <param name="Capped">
/// True when more entries match than <see cref="OperationsService.LogCountCap"/>, so the count
/// is a lower bound: "10,000+". Only a query with a text filter is capped.
/// </param>
public record LogCount(int Count, bool Capped);
