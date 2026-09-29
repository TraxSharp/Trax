using System.Reflection;
using Scriban;
using Scriban.Runtime;
using Trax.Cli.Models;

namespace Trax.Cli.Generator;

public partial class CodeRenderer
{
    private readonly Dictionary<string, Template> _templates = new();
    private string? _modelsNamespace;
    private HashSet<string> _modelNames = new(StringComparer.Ordinal);
    private const string UnitType = "global::LanguageExt.Unit";

    public CodeRenderer()
    {
        LoadTemplates();
    }

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

    public string RenderTrainInterface(ApiOperation operation, string projectName)
    {
        var isUnit = IsUnitOutput(operation.OutputType);
        var ns = $"{projectName}.Trains.{operation.Group}.{operation.Name}";
        var outputName = isUnit ? UnitType : QualifyModels(operation.OutputType.Name);
        return Render(
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
        );
    }

    public string RenderTrainImplementation(ApiOperation operation, string projectName)
    {
        var isUnit = IsUnitOutput(operation.OutputType);
        var ns = $"{projectName}.Trains.{operation.Group}.{operation.Name}";
        var attribute = operation.Kind == OperationKind.Query ? "TraxQuery" : "TraxMutation";
        var description = operation.Description ?? $"{operation.Name} operation";
        var outputName = isUnit ? UnitType : QualifyModels(operation.OutputType.Name);
        return Render(
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
        );
    }

    public string RenderInput(ApiOperation operation, string projectName)
    {
        var ns = $"{projectName}.Trains.{operation.Group}.{operation.Name}";
        return Render(
            "Input",
            new
            {
                Namespace = ns,
                TypeName = operation.InputType.Name,
                Fields = operation.InputType.Fields.Select(QualifiedField).ToList(),
                HasFields = operation.InputType.Fields.Count > 0,
            }
        );
    }

    public string RenderOutput(ApiOperation operation, string projectName)
    {
        var ns = $"{projectName}.Trains.{operation.Group}.{operation.Name}";
        return Render(
            "Output",
            new
            {
                Namespace = ns,
                TypeName = operation.OutputType.Name,
                Fields = operation.OutputType.Fields.Select(QualifiedField).ToList(),
                HasFields = operation.OutputType.Fields.Count > 0,
            }
        );
    }

    public string RenderJunction(ApiOperation operation, string projectName)
    {
        var isUnit = IsUnitOutput(operation.OutputType);
        var ns = $"{projectName}.Trains.{operation.Group}.{operation.Name}";
        var outputName = isUnit ? UnitType : QualifyModels(operation.OutputType.Name);
        return Render(
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
        );
    }

    public string RenderTypeRecord(ApiType type, string projectName, string? group)
    {
        var ns =
            group != null ? $"{projectName}.Trains.Models.{group}" : $"{projectName}.Trains.Models";
        return Render(
            "TypeRecord",
            new
            {
                Namespace = ns,
                TypeName = type.Name,
                Fields = type.Fields.Select(MapField).ToList(),
                HasFields = type.Fields.Count > 0,
            }
        );
    }

    public string RenderEnum(ApiEnum apiEnum, string projectName)
    {
        return Render(
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
        );
    }

    public string RenderTrainsCsproj()
    {
        return Render("TrainsCsproj", new { });
    }

    public string RenderGraphQLNamespaces(IEnumerable<string> groups, string projectName)
    {
        var scriptObject = new ScriptObject();
        scriptObject["Namespace"] = $"{projectName}.Trains";

        var groupList = new ScriptArray();
        foreach (var group in groups)
        {
            var obj = new ScriptObject
            {
                ["name"] = group,
                ["value"] = NamingConventions.ToCamelCase(group),
            };
            groupList.Add(obj);
        }

        scriptObject["Groups"] = groupList;

        if (!_templates.TryGetValue("GraphQLNamespaces", out var template))
            throw new InvalidOperationException("Template 'GraphQLNamespaces' not found.");

        var context = new TemplateContext();
        context.PushGlobal(scriptObject);

        return template.Render(context);
    }

    public string RenderManifestNames(List<ApiOperation> operations, string projectName)
    {
        var scriptObject = new ScriptObject();
        scriptObject["project_name"] = projectName;

        var opList = new ScriptArray();
        foreach (var op in operations)
        {
            var opObj = new ScriptObject
            {
                ["name"] = op.Name,
                ["kebab_name"] = NamingConventions.ToKebabCase(op.Name),
            };
            opList.Add(opObj);
        }

        scriptObject["operations"] = opList;

        if (!_templates.TryGetValue("ManifestNames", out var template))
            throw new InvalidOperationException("Template 'ManifestNames' not found.");

        var context = new TemplateContext();
        context.PushGlobal(scriptObject);

        return template.Render(context);
    }

    private static ScriptObject MapField(ApiField field)
    {
        var sanitizedName = NamingConventions.SanitizeIdentifier(field.Name);
        var obj = new ScriptObject
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

        return ModelName()
            .Replace(
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
        outputType.Name == "Unit"
        || (outputType.IsBuiltIn && outputType.Fields.Count == 0 && outputType.Name == "Unit");

    private string Render(string templateName, object model)
    {
        if (!_templates.TryGetValue(templateName, out var template))
            throw new InvalidOperationException($"Template '{templateName}' not found.");

        var scriptObject = new ScriptObject();
        scriptObject.Import(model, renamer: member => member.Name);

        var context = new TemplateContext();
        context.PushGlobal(scriptObject);

        return template.Render(context);
    }

    private void LoadTemplates()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var prefix = "Trax.Cli.Templates.";

        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.StartsWith(prefix) || !resourceName.EndsWith(".sbn"))
                continue;

            var templateName = resourceName[prefix.Length..^4]; // strip prefix and .sbn
            using var stream = assembly.GetManifestResourceStream(resourceName)!;
            using var reader = new StreamReader(stream);
            var content = reader.ReadToEnd();

            _templates[templateName] = Template.Parse(content, resourceName);
        }
    }

    [System.Text.RegularExpressions.GeneratedRegex(SchemaNames.Pattern)]
    private static partial System.Text.RegularExpressions.Regex ModelName();
}
