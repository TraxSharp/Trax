using HotChocolate;
using HotChocolate.Types;

namespace Trax.Api.Tests.GraphQLClient.Fixtures;

/// <summary>
/// A deliberately small HotChocolate schema exercised by every GraphQL client integration
/// test. Each shape (scalar, list, nullable, enum, input object, mutation) is included so
/// the test matrix can cover the full surface without needing a sprawling fake domain.
/// </summary>
public enum Rank
{
    Bronze,
    Silver,
    Gold,
}
