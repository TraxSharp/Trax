using HotChocolate.Execution.Configuration;

namespace Trax.Samples.GraphQLClient.Schema;

public record Item(string Id, string Name, ItemCategory Category);
