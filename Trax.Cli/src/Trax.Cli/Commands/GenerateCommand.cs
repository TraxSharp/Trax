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

        var forceOption = CreateForceOption();

        var command = new Command("generate", "Generate a Trax API project from a schema file")
        {
            schemaOption,
            outputOption,
            nameOption,
            typeOption,
            forceOption,
        };

        command.SetAction(
            (parseResult, _) =>
            {
                var schema = parseResult.GetRequiredValue(schemaOption);
                var output = parseResult.GetRequiredValue(outputOption);
                var name = parseResult.GetRequiredValue(nameOption);
                var type = parseResult.GetValue(typeOption);
                var force = parseResult.GetValue(forceOption);

                return HandleAsync(
                    schema,
                    output,
                    name,
                    type,
                    force,
                    parseResult.InvocationConfiguration.Output,
                    parseResult.InvocationConfiguration.Error
                );
            }
        );

        return command;
    }

    private static Option<bool> CreateForceOption() =>
        new("--force")
        {
            Description =
                "Replace the output directory if it exists, once generation has succeeded (refused for the current directory, its parents, or a git repository)",
        };

    // Returns the exit code as well as setting Environment.ExitCode: Program returns InvokeAsync's
    // result, which wins over Environment.ExitCode, so an action that only set it exited 0.
    internal static async Task<int> HandleAsync(
        FileInfo schema,
        DirectoryInfo output,
        string name,
        string? type,
        bool force,
        TextWriter stdout,
        TextWriter stderr,
        TraxProjectGenerator? generator = null
    )
    {
        if (!await TraxProjectGenerator.IsDotnetAvailableAsync().ConfigureAwait(false))
        {
            await stderr
                .WriteLineAsync("The 'dotnet' CLI is not available. Please install the .NET SDK.")
                .ConfigureAwait(false);
            return Fail();
        }

        if (!schema.Exists)
        {
            await stderr
                .WriteLineAsync($"Schema file not found: {schema.FullName}")
                .ConfigureAwait(false);
            return Fail();
        }

        if (output.Exists && !force)
        {
            await stderr
                .WriteLineAsync(
                    $"Output directory already exists: {output.FullName}. Use --force to overwrite."
                )
                .ConfigureAwait(false);
            return Fail();
        }

        string schemaType;
        try
        {
            schemaType = SchemaDetector.Detect(schema.FullName, type);
        }
        catch (ArgumentException ex)
        {
            await stderr.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return Fail();
        }

        if (
            !await ParseAndGenerateAsync(
                    schema,
                    output,
                    name,
                    force,
                    schemaType,
                    stdout,
                    stderr,
                    generator
                )
                .ConfigureAwait(false)
        )
            return Fail();

        await WriteNextStepsAsync(output, name, stdout).ConfigureAwait(false);
        return 0;
    }

    // Parses the schema and generates the project; false once the failure is written to stderr.
    private static async Task<bool> ParseAndGenerateAsync(
        FileInfo schema,
        DirectoryInfo output,
        string name,
        bool force,
        string schemaType,
        TextWriter stdout,
        TextWriter stderr,
        TraxProjectGenerator? generator
    )
    {
        try
        {
            ISchemaParser parser = schemaType switch
            {
                "graphql" => new GraphQLSchemaParser(stdout),
                "openapi" => new OpenApiSchemaParser(stderr),
                _ => throw new InvalidOperationException($"Unsupported schema type: {schemaType}"),
            };

            var apiSchema = await parser.ParseAsync(schema.FullName).ConfigureAwait(false);

            await stdout
                .WriteLineAsync(
                    $"Parsed {apiSchema.Operations.Count} operations, "
                        + $"{apiSchema.Types.Count} types, "
                        + $"{apiSchema.Enums.Count} enums from {schemaType} schema."
                )
                .ConfigureAwait(false);

            await (generator ?? new TraxProjectGenerator())
                .GenerateAsync(apiSchema, output.FullName, name, force)
                .ConfigureAwait(false);
            return true;
        }
        catch (InvalidOperationException ex)
        {
            await stderr.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return false;
        }
    }

    private static async Task WriteNextStepsAsync(
        DirectoryInfo output,
        string name,
        TextWriter stdout
    )
    {
        await stdout
            .WriteLineAsync($"Generated Trax project at: {output.FullName}")
            .ConfigureAwait(false);
        await stdout
            .WriteLineAsync($"  {name}.Hub/     — Hub project (API + Scheduler + Dashboard)")
            .ConfigureAwait(false);
        await stdout
            .WriteLineAsync($"  {name}.Trains/  — Trains library (generated from schema)")
            .ConfigureAwait(false);
        await stdout.WriteLineAsync().ConfigureAwait(false);
        await stdout.WriteLineAsync("Next steps:").ConfigureAwait(false);
        await stdout.WriteLineAsync($"  cd {output.FullName}/{name}.Hub").ConfigureAwait(false);
        await stdout.WriteLineAsync("  dotnet restore").ConfigureAwait(false);
        await stdout
            .WriteLineAsync("  # Fill in junction implementations (search for TODO)")
            .ConfigureAwait(false);
        await stdout.WriteLineAsync("  dotnet run").ConfigureAwait(false);
    }

    private static int Fail()
    {
        Environment.ExitCode = 1;
        return 1;
    }
}
