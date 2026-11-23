using Trax.Scheduler.Services.Operations;

namespace Trax.Api.DTOs;

/// <summary>
/// One sticky: its flow role (<c>actor</c>, <c>command</c>, <see langword="event"/>, <c>policy</c>, <c>aggregate</c> or
/// <c>view</c>), its lane, its label, and the scenarios that produced it, none when the host file has no provenance.
/// </summary>
public record MachineSticky(
    string Id,
    string Kind,
    string Machine,
    string Label,
    IReadOnlyList<MachineScenario> Scenarios
);
