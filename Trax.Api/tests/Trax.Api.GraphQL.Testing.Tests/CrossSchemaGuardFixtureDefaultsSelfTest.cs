using Microsoft.EntityFrameworkCore;
using Trax.Api.GraphQL.DataLoaders.CrossSchema;
using Trax.Core.Testing;

namespace Trax.Api.GraphQL.Testing.Tests;

/// <summary>
/// The fixture with nothing configured: it scans this repository from its root with an empty edge
/// manifest. Trax.Api has no cross-schema project, so it opts out of expecting resolvers; any type
/// extension in the repository that reads off its parent is still checked.
/// </summary>
[TestFixture]
public sealed class CrossSchemaGuardFixtureDefaultsSelfTest : CrossSchemaGuardFixture
{
    protected override bool ExpectsCrossSchemaResolvers => false;

    protected override bool ExpectsParentResolvers => false;
}
