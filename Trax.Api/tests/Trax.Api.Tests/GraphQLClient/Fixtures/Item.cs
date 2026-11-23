using HotChocolate;
using HotChocolate.Types;

namespace Trax.Api.Tests.GraphQLClient.Fixtures;

public record Item(string Id, string Name, ItemCategory Category);
