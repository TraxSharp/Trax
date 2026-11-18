using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Trax.Api.GraphQL.TypeModules;

namespace Trax.Api.GraphQL.PersistedOperations.Services;

/// <summary>
/// The instruments that alert on a machine document whose stored body does not match its id: a lookup refused, and a
/// row startup wrote back.
/// </summary>
internal static class MachineDocumentInstruments
{
    /// <summary>The meter both instruments belong to.</summary>
    public const string MeterName = "Trax.Api.PersistedOperations";

    private static readonly Meter Meter = new(MeterName);

    /// <summary>A lookup refused because the stored body does not hash to its id.</summary>
    public static readonly Counter<long> Mismatches = Meter.CreateCounter<long>(
        "trax.persisted_operations.machine_document_mismatches",
        description: "Lookups of a machine document refused because the stored body does not match its id."
    );

    /// <summary>A stored machine document startup wrote back because its body did not match its id.</summary>
    public static readonly Counter<long> Repairs = Meter.CreateCounter<long>(
        "trax.persisted_operations.machine_document_repairs",
        description: "Machine documents written back at startup because the stored body did not match its id."
    );
}
