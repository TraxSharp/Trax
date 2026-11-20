using System.Reflection;
using Scriban;
using Scriban.Runtime;
using Trax.Cli.Models;
using Trax.Core.Extensions;

namespace Trax.Cli.Generator;

public partial class CodeRenderer
{
    private Dictionary<string, Template>? _templates;
    private string? _modelsNamespace;
    private HashSet<string> _modelNames = new(StringComparer.Ordinal);
    private const string UnitType = "global::Trax.Core.Functional.Unit";

    /// <summary>
    /// Sets the models namespace to include as a using directive in generated files.
    /// Call this when the schema has shared model types (non-built-in types or enums).
    /// </summary>
    public void SetModelsNamespace(string modelsNamespace) => _modelsNamespace = modelsNamespace;

    /// <summary>
    /// The shared model types (under <see cref="SetModelsNamespace"/>). Code outside the models namespace
    /// refers to each one fully qualified rather than through a using directive, so a model named like a
    /// framework type (<c>Task</c>, <c>File</c>) is never ambiguous with it.
    /// </summary>
    internal void SetModelNames(IEnumerable<string> names) =>
        _modelNames = new HashSet<string>(names, StringComparer.Ordinal);

    public async Task<string> RenderTrainInterfaceAsync(ApiOperation operation, string projectName)
    {
        var isUnit = IsUnitOutput(operation.OutputType);
        var ns = $"{projectName}.Trains.{operation.Group}.{operation.Name}";
        var outputName = isUnit ? UnitType : QualifyModels(operation.OutputType.Name);
        return await RenderAsync(
                "TrainInterface",
                new
                {
                    Namespace = ns,
                    TrainName = operation.Name,
                    OutputTypeName = outputName,
                    OutputIsUnit = isUnit,
                    InputTypeName = operation.InputType.Name,
                    InputIsUnit = false,
                }
            )
            .ConfigureAwait(false);
    }

    public async Task<string> RenderTrainImplementationAsync(
        ApiOperation operation,
        string projectName
    )
    {
        var isUnit = IsUnitOutput(operation.OutputType);
        var ns = $"{projectName}.Trains.{operation.Group}.{operation.Name}";
        var attribute = operation.Kind == OperationKind.Query ? "TraxQuery" : "TraxMutation";
        var description = operation.Description ?? $"{operation.Name} operation";
        var outputName = isUnit ? UnitType : QualifyModels(operation.OutputType.Name);
        return await RenderAsync(
                "TrainImplementation",
                new
                {
                    Namespace = ns,
                    TrainName = operation.Name,
                    InputTypeName = operation.InputType.Name,
                    OutputTypeName = outputName,
                    OutputIsUnit = isUnit,
                    InputIsUnit = false,
                    Attribute = attribute,
                    DocDescription = GeneratedText.DocComment(description),
                    Description = GeneratedText.StringLiteralContent(description),
                    GraphQLNamespace = operation.Group,
                    TrainsNamespace = $"{projectName}.Trains",
                }
            )
            .ConfigureAwait(false);
    }

    public async Task<string> RenderInputAsync(ApiOperation operation, string projectName)
    {
        var ns = $"{projectName}.Trains.{operation.Group}.{operation.Name}";
        return await RenderAsync(
                "Input",
                new
                {
                    Namespace = ns,
                    TypeName = operation.InputType.Name,
                    Fields = operation.InputType.Fields.Select(QualifiedField).ToList(),
                    HasFields = operation.InputType.Fields.Count > 0,
                }
            )
            .ConfigureAwait(false);
    }

    public async Task<string> RenderOutputAsync(ApiOperation operation, string projectName)
    {
        var ns = $"{projectName}.Trains.{operation.Group}.{operation.Name}";
        return await RenderAsync(
                "Output",
                new
                {
                    Namespace = ns,
                    TypeName = operation.OutputType.Name,
                    Fields = operation.OutputType.Fields.Select(QualifiedField).ToList(),
                    HasFields = operation.OutputType.Fields.Count > 0,
                }
            )
            .ConfigureAwait(false);
    }

    public async Task<string> RenderJunctionAsync(ApiOperation operation, string projectName)
    {
        var isUnit = IsUnitOutput(operation.OutputType);
        var ns = $"{projectName}.Trains.{operation.Group}.{operation.Name}";
        var outputName = isUnit ? UnitType : QualifyModels(operation.OutputType.Name);
        return await RenderAsync(
                "Junction",
                new
                {
                    Namespace = ns,
                    TrainName = operation.Name,
                    InputTypeName = operation.InputType.Name,
                    OutputTypeName = outputName,
                    OutputIsUnit = isUnit,
                    InputIsUnit = false,
                    HttpMethod = operation.HttpMethod is null
                        ? null
                        : GeneratedText.Comment(operation.HttpMethod),
                    HttpPath = operation.HttpPath is null
                        ? null
                        : GeneratedText.Comment(operation.HttpPath),
                }
            )
            .ConfigureAwait(false);
    }

    public async Task<string> RenderTypeRecordAsync(ApiType type, string projectName, string? group)
    {
        var ns =
            group != null ? $"{projectName}.Trains.Models.{group}" : $"{projectName}.Trains.Models";
        return await RenderAsync(
                "TypeRecord",
                new
                {
                    Namespace = ns,
                    TypeName = type.Name,
                    Fields = type.Fields.Select(MapField).ToList(),
                    HasFields = type.Fields.Count > 0,
                }
            )
            .ConfigureAwait(false);
    }

    public async Task<string> RenderEnumAsync(ApiEnum apiEnum, string projectName)
    {
        return await RenderAsync(
                "Enum",
                new
                {
                    Namespace = $"{projectName}.Trains.Models",
                    EnumName = apiEnum.Name,
                    Values = apiEnum.Values.Select(NamingConventions.SanitizeIdentifier).ToList(),
                    Description = apiEnum.Description is null
                        ? null
                        : GeneratedText.DocComment(apiEnum.Description),
                }
            )
            .ConfigureAwait(false);
    }

    /// <summary>The Trax packages the trains library references, in the order its csproj lists them.</summary>
    internal static readonly IReadOnlyList<string> TrainsPackages =
    [
        "Trax.Effect",
        "Trax.Effect.Data.InMemory",
        "Trax.Mediator",
        "Trax.Scheduler",
    ];

    public Task<string> RenderTrainsCsprojAsync() =>
        RenderTrainsCsprojAsync(
            TrainsPackages.ToDictionary(p => p, _ => "1.*", StringComparer.Ordinal)
        );

    /// <summary>The trains csproj, referencing each of <see cref="TrainsPackages"/> at the version given.</summary>
    internal async Task<string> RenderTrainsCsprojAsync(
        IReadOnlyDictionary<string, string> versions
    )
    {
        var scriptObject = new ScriptObject(StringComparer.Ordinal);
        var packages = new ScriptArray();
        foreach (var name in TrainsPackages)
        {
            var package = new ScriptObject(StringComparer.Ordinal);
            package["Name"] = name;
            package["Version"] = versions[name];
            packages.Add(package);
        }
        scriptObject["Packages"] = packages;

        var context = new TemplateContext(StringComparer.Ordinal);
        context.PushGlobal(scriptObject);
        return await (await TemplateAsync("TrainsCsproj").ConfigureAwait(false))
            .RenderAsync(context)
            .ConfigureAwait(false);
    }

    public async Task<string> RenderGraphQLNamespacesAsync(
        IEnumerable<string> groups,
        string projectName
    )
    {
        var scriptObject = new ScriptObject(StringComparer.Ordinal);
        scriptObject["Namespace"] = $"{projectName}.Trains";

        var groupList = new ScriptArray();
        foreach (var group in groups)
        {
            var obj = new ScriptObject(StringComparer.Ordinal)
            {
                ["name"] = group,
                ["value"] = NamingConventions.ToCamelCase(group),
            };
            groupList.Add(obj);
        }

        scriptObject["Groups"] = groupList;

        var template = await TemplateAsync("GraphQLNamespaces").ConfigureAwait(false);

        var context = new TemplateContext(StringComparer.Ordinal);
        context.PushGlobal(scriptObject);

        return await template.RenderAsync(context).ConfigureAwait(false);
    }

    public async Task<string> RenderManifestNamesAsync(
        IList<ApiOperation> operations,
        string projectName
    )
    {
        var scriptObject = new ScriptObject(StringComparer.Ordinal);
        scriptObject["project_name"] = projectName;

        var opList = new ScriptArray();
        foreach (var op in operations)
        {
            var opObj = new ScriptObject(StringComparer.Ordinal)
            {
                ["name"] = op.Name,
                ["kebab_name"] = NamingConventions.ToKebabCase(op.Name),
            };
            opList.Add(opObj);
        }

        scriptObject["operations"] = opList;

        var template = await TemplateAsync("ManifestNames").ConfigureAwait(false);

        var context = new TemplateContext(StringComparer.Ordinal);
        context.PushGlobal(scriptObject);

        return await template.RenderAsync(context).ConfigureAwait(false);
    }

    private static ScriptObject MapField(ApiField field)
    {
        var sanitizedName = NamingConventions.SanitizeIdentifier(field.Name);
        var obj = new ScriptObject(StringComparer.Ordinal)
        {
            ["Name"] = field.Name,
            ["SanitizedName"] = sanitizedName,
            ["TypeName"] = field.TypeName,
            ["IsRequired"] = field.IsRequired,
            ["IsNullable"] = field.IsNullable,
            ["Description"] =
                field.Description != null ? GeneratedText.DocComment(field.Description) : null,
            ["RequiredKeyword"] = field.IsRequired ? "required " : "",
            ["NullableMarker"] = field.IsNullable && !field.TypeName.EndsWith('?') ? "?" : "",
        };
        return obj;
    }

    private ScriptObject QualifiedField(ApiField field)
    {
        var obj = MapField(field);
        obj["TypeName"] = QualifyModels(field.TypeName);
        return obj;
    }

    /// <summary>
    /// Rewrites every model name in a type expression (<c>Player</c>, <c>List&lt;Player&gt;</c>) to its
    /// <c>global::</c>-qualified name. A name followed by type arguments is a framework generic
    /// (<c>List&lt;T&gt;</c>), never a model: models are not generic.
    /// </summary>
    private string QualifyModels(string typeExpression)
    {
        if (_modelsNamespace == null || _modelNames.Count == 0)
            return typeExpression;

        return ModelName.Replace(
            typeExpression,
            m =>
                _modelNames.Contains(m.Value) && !FollowedByTypeArguments(typeExpression, m)
                    ? $"global::{_modelsNamespace}.{m.Value}"
                    : m.Value
        );
    }

    private static bool FollowedByTypeArguments(
        string text,
        System.Text.RegularExpressions.Match m
    ) => m.Index + m.Length < text.Length && text[m.Index + m.Length] == '<';

    private static bool IsUnitOutput(ApiType outputType) =>
        string.Equals(outputType.Name, "Unit", StringComparison.Ordinal)
        || (
            outputType.IsBuiltIn
            && outputType.Fields.Count == 0
            && string.Equals(outputType.Name, "Unit", StringComparison.Ordinal)
        );

    private async Task<string> RenderAsync(string templateName, object model)
    {
        var template = await TemplateAsync(templateName).ConfigureAwait(false);

        var scriptObject = new ScriptObject(StringComparer.Ordinal);
        scriptObject.Import(model, renamer: member => member.Name);

        var context = new TemplateContext(StringComparer.Ordinal);
        context.PushGlobal(scriptObject);

        return await template.RenderAsync(context).ConfigureAwait(false);
    }

    // The templates are read from the assembly's resources once, on first use.
    private async Task<Template> TemplateAsync(string templateName)
    {
        _templates ??= await LoadTemplatesAsync().ConfigureAwait(false);
        return _templates.TryGetValue(templateName, out var template)
            ? template
            : throw new InvalidOperationException($"Template '{templateName}' not found.");
    }

    private static async Task<Dictionary<string, Template>> LoadTemplatesAsync()
    {
        var templates = new Dictionary<string, Template>(StringComparer.Ordinal);
        var assembly = Assembly.GetExecutingAssembly();
        var prefix = "Trax.Cli.Templates.";

        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (
                !resourceName.StartsWith(prefix, StringComparison.Ordinal)
                || !resourceName.EndsWith(".sbn", StringComparison.Ordinal)
            )
                continue;

            var templateName = resourceName[prefix.Length..^4]; // strip prefix and .sbn
            var stream = assembly.GetManifestResourceStream(resourceName).NotNull();
            await using (stream.ConfigureAwait(false))
            {
                using var reader = new StreamReader(stream);
                var content = await reader.ReadToEndAsync().ConfigureAwait(false);
                templates[templateName] = Template.Parse(content, resourceName);
            }
        }
        return templates;
    }

    [System.Text.RegularExpressions.GeneratedRegex(
        SchemaNames.Pattern,
        System.Text.RegularExpressions.RegexOptions.None,
        matchTimeoutMilliseconds: 1000
    )]
    private static partial System.Text.RegularExpressions.Regex ModelName { get; }
}
