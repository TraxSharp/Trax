using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Trax.Effect.Broadcaster.RabbitMQ;

/// <summary>What <see cref="BroadcastMessageSignature.Verify"/> found.</summary>
internal enum BroadcastSignatureVerdict
{
    /// <summary>The signature matches and its timestamp is within the allowed skew.</summary>
    Accepted,

    /// <summary>The message carries no signature.</summary>
    Missing,

    /// <summary>The signature is malformed or does not match the message.</summary>
    Invalid,

    /// <summary>The signature matches, but its timestamp is further from this clock than allowed.</summary>
    Stale,
}
