// What the examples under sdk-reference/persisted-operations/ take as given: the package's
// namespaces, the GraphQL builder extension, and a manifest of documents to upload.
global using static PersistedOperationSamples;
global using Trax.Api.GraphQL.Extensions;
global using Trax.Api.GraphQL.PersistedOperations.Broadcasting;
global using Trax.Api.GraphQL.PersistedOperations.Configuration;
global using Trax.Api.GraphQL.PersistedOperations.Extensions;
global using Trax.Api.GraphQL.PersistedOperations.GraphQL.Models;
global using Trax.Api.GraphQL.PersistedOperations.Services;
global using Trax.Api.GraphQL.PersistedOperations.ShapeDiff;
global using Trax.Api.GraphQL.PersistedOperations.Storage;
global using Trax.Api.GraphQL.PersistedOperations.Storage.Exceptions;
global using Trax.Api.GraphQL.PersistedOperations.Storage.Validation;

public static class PersistedOperationSamples
{
    /// <summary>The documents a CI job uploads, by id.</summary>
    public static IReadOnlyDictionary<string, string> manifest { get; } =
        new Dictionary<string, string>
        {
            ["userProfile_v1"] = "query UserProfile($id: Int!) { user(id: $id) { id name } }",
        };

    public static string candidateDocument => manifest["userProfile_v1"];
}
