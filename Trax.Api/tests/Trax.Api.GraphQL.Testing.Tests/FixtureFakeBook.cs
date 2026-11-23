using Microsoft.EntityFrameworkCore;
using Trax.Api.GraphQL.DataLoaders.CrossSchema;
using Trax.Core.Testing;

namespace Trax.Api.GraphQL.Testing.Tests;

// Fake types backing a valid edge for the self-test (top-level so NUnit does not inspect them as
// nested members of the fixture).
public sealed class FixtureFakeBook
{
    public int Id { get; set; }
}
