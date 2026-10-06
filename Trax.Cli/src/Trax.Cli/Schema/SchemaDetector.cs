namespace Trax.Cli.Schema;

public static class SchemaDetector
{
    private static readonly HashSet<string> GraphQLExtensions = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ".graphql",
        ".gql",
    };

    private static readonly HashSet<string> OpenApiExtensions = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ".json",
        ".yaml",
        ".yml",
    };

    public static string Detect(string filePath, string? explicitType = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitType))
        {
            return explicitType.ToLowerInvariant() switch
            {
                "graphql" => "graphql",
                "openapi" => "openapi",
                _ => throw new ArgumentException(
                    $"Unknown schema type '{explicitType}'. Supported types: graphql, openapi."
                ),
            };
        }

        var extension = Path.GetExtension(filePath);

        if (GraphQLExtensions.Contains(extension))
            return "graphql";

        if (OpenApiExtensions.Contains(extension))
            return "openapi";

        throw new ArgumentException(
            $"Cannot detect schema type from extension '{extension}'. "
                + "Use --type to specify explicitly. Supported types: graphql, openapi."
        );
    }
}
