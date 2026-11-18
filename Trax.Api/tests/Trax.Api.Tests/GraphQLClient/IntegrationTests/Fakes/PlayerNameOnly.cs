using System.Text.Json.Serialization;
using Trax.Api.GraphQL.Client;

namespace Trax.Api.Tests.GraphQLClient.IntegrationTests.Fakes;

/// <summary>
/// Strict-extract drift fixture. The query selects <c>id</c>, <c>name</c>, <c>level</c> but
/// the POCO omits <c>level</c>. With strictness on, this should throw or warn.
/// </summary>
public record PlayerNameOnly([property: JsonPropertyName("id")] string Id, string Name);
