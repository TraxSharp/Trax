using HotChocolate;
using HotChocolate.Types;

namespace Trax.Api.Tests.GraphQLClient.Fixtures;

public record RenamePlayerInput(string Id, string NewName);
