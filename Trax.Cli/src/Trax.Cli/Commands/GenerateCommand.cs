using System.CommandLine;
using Trax.Cli.Generator;
using Trax.Cli.Schema;
using Trax.Cli.Schema.GraphQL;
using Trax.Cli.Schema.OpenApi;

namespace Trax.Cli.Commands;

public static class GenerateCommand
{
    public static Command Create()
    {
        var schemaOption = new Option<FileInfo>("--schema")
        {
            Description = "Path to the schema file (.graphql, .gql, .json, .yaml, .yml)",
            Required = true,
        };

        var outputOption = new Option<DirectoryInfo>("--output")
        {
            Description = "Output directory for the generated project",
            Required = true,
        };

        var nameOption = new Option<string>("--name")
        {
            Description = "Project name (used for namespace and csproj)",
            Required = true,
        };

        var typeOption = new Option<string?>("--type")
        {
            Description =
                "Schema type: graphql or openapi (auto-detected from extension if omitted)",
        };

        var forceOption = new Option<bool>("--force")
        {
            Description = "Overwrite the output directory if it exists",
        };

        var command = new Command("generate", "Generate a Trax API project from a schema file")
        {
            schemaOption,
            outputOption,
            nameOption,
            typeOption,
            forceOption,
        };

        command.SetAction(parseResult =>
        {
            var schema = parseResult.GetRequiredValue(schemaOption);
            var output = parseResult.GetRequiredValue(outputOption);
            var name = parseResult.GetRequiredValue(nameOption);
            var type = parseResult.GetValue(typeOption);
            var force = parseResult.GetValue(forceOption);

            Handle(schema, output, name, type, force);
        });

        return command;
    }

    private static void Handle(
        FileInfo schema,
        DirectoryInfo output,
        string name,
        string? type,
        bool force
    )
    {
        if (!TraxProjectGenerator.IsDotnetAvailable())
        {
            Console.Error.WriteLine(
                "The 'dotnet' CLI is not available. Please install the .NET SDK."
            );
            Environment.ExitCode = 1;
            return;
        }

        if (!schema.Exists)
        {
            Console.Error.WriteLine($"Schema file not found: {schema.FullName}");
            Environment.ExitCode = 1;
            return;
        }

        if (output.Exists && !force)
        {
            Console.Error.WriteLine(
                $"Output directory already exists: {output.FullName}. Use --force to overwrite."
            );
            Environment.ExitCode = 1;
            return;
        }

        var schemaType = SchemaDetector.Detect(schema.FullName, type);

        ISchemaParser parser = schemaType switch
        {
            "graphql" => new GraphQLSchemaParser(),
            "openapi" => new OpenApiSchemaParser(),
            _ => throw new InvalidOperationException($"Unsupported schema type: {schemaType}"),
        };

        var apiSchema = parser.Parse(schema.FullName);

        Console.WriteLine(
            $"Parsed {apiSchema.Operations.Count} operations, "
                + $"{apiSchema.Types.Count} types, "
                + $"{apiSchema.Enums.Count} enums from {schemaType} schema."
        );

        var generator = new TraxProjectGenerator();
        generator.Generate(apiSchema, output.FullName, name, force);

        Console.WriteLine($"Generated Trax project at: {output.FullName}");
        Console.WriteLine($"  {name}.Api/     — API project (from trax-api template)");
        Console.WriteLine($"  {name}.Trains/  — Trains library (generated from schema)");
        Console.WriteLine();
        Console.WriteLine("Next steps:");
        Console.WriteLine($"  cd {output.FullName}/{name}.Api");
        Console.WriteLine("  dotnet restore");
        Console.WriteLine("  # Fill in junction implementations (search for TODO)");
        Console.WriteLine("  dotnet run");
    }
}
