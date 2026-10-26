using Trax.Api.GraphQL.PersistedOperations.Configuration;

namespace Trax.Api.Tests.PersistedOperations.Fixtures;

/// <summary>The invalidation signing key the tests share: 32 bytes, so long enough.</summary>
internal static class TestInvalidationKey
{
    public static readonly byte[] Value = "trax-api-tests-invalidation-key!"u8.ToArray();

    /// <summary>Sets <see cref="Value"/> as the signing key.</summary>
    public static void Sign(RabbitMqInvalidationOptions options) => options.SigningKey = Value;
}
