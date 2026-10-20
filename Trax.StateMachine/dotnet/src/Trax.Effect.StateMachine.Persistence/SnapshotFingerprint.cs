using System.Security.Cryptography;
using System.Text;

namespace Trax.Effect.StateMachine.Persistence;

/// <summary>
/// The fingerprint an effect's claim records of the content the effect ran on: the lowercase hex SHA-256 of the
/// snapshot's canonical wire (<see cref="ISnapshotDraftService.Serialize"/>). Two snapshots have the same
/// fingerprint exactly when their canonical wire is byte for byte the same, so key order and number formatting in
/// what a client sent do not change it.
/// </summary>
public static class SnapshotFingerprint
{
    /// <summary>The fingerprint of a snapshot, given its canonical wire.</summary>
    /// <param name="canonicalWire">The snapshot as <see cref="ISnapshotDraftService.Serialize"/> writes it.</param>
    /// <returns>64 lowercase hex characters.</returns>
    public static string Of(string canonicalWire) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalWire)));
}
