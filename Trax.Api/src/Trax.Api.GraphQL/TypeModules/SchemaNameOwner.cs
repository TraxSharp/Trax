using Trax.Api.GraphQL.Configuration;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Api.GraphQL.TypeModules;

/// <summary>
/// Who puts a field on a type, or names a type, that Trax builds.
/// </summary>
/// <param name="Key">Identifies the owner, so one owner contributing a name twice is not a clash.</param>
/// <param name="Description">How the refusal names it.</param>
internal sealed record SchemaNameOwner(string Key, string Description);
