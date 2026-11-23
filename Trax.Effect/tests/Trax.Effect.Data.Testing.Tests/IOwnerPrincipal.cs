using Microsoft.EntityFrameworkCore;
using Trax.Effect.Attributes;

namespace Trax.Effect.Data.Testing.Tests;

// A consumer's shape for the owner-scope census: an owner entity (Account), the accessor a row
// filter reads the current principal through (IOwnerPrincipal), and entities that hold per-user
// data in each of the ways the census has to recognise. Each context maps one scenario, so a test
// names exactly the offender it expects.

public interface IOwnerPrincipal
{
    int? CurrentAccountId { get; }
}
