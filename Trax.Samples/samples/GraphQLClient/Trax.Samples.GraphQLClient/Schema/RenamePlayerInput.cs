using HotChocolate.Execution.Configuration;

namespace Trax.Samples.GraphQLClient.Schema;

public record RenamePlayerInput(string Id, string NewName);
