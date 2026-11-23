using Microsoft.EntityFrameworkCore;
using Trax.Api.GraphQL.DataLoaders.CrossSchema;
using Trax.Core.Testing;

namespace Trax.Api.GraphQL.Testing.Tests;

public sealed class FixtureFakeLoan
{
    public int Id { get; set; }
    public int BookId { get; set; }
}
