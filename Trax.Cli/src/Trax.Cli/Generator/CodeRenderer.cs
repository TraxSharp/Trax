using System.Reflection;
using Scriban;
using Scriban.Runtime;
using Trax.Cli.Models;

namespace Trax.Cli.Generator;

public class CodeRenderer
{
    private readonly Dictionary<string, Template> _templates = new();

    public CodeRenderer()
    {
        LoadTemplates();
    }

    public string RenderTrainInterface(ApiOperation operation, string projectName)
    {
        var isUnit = IsUnitOutput(operation.OutputType);
        var ns = $"{projectName}.Trains.{operation.Group}.{operation.Name}";
        return Render(
            "TrainInterface",
            new
            {
                Namespace = ns,
                TrainName = operation.Name,
                InputTypeName = operation.InputType.Fields.Count > 0
                    ? operation.InputType.Name
                    : "Unit",
                OutputTypeName = isUnit ? "Unit" : operation.OutputType.Name,
                OutputIsUnit = isUnit,
                InputIsUnit = operation.InputType.Fields.Count == 0,
            }
        );
    }

    public string RenderTrainImplementation(ApiOperation operation, string projectName)
    {
        var isUnit = IsUnitOutput(operation.OutputType);
        var ns = $"{projectName}.Trains.{operation.Group}.{operation.Name}";
        var attribute = operation.Kind == OperationKind.Query ? "TraxQuery" : "TraxMutation";
        return Render(
            "TrainImplementation",
            new
            {
                Namespace = ns,
                TrainName = operation.Name,
                InputTypeName = operation.InputType.Fields.Count > 0
                    ? operation.InputType.Name
                    : "Unit",
                OutputTypeName = isUnit ? "Unit" : operation.OutputType.Name,
                OutputIsUnit = isUnit,
                InputIsUnit = operation.InputType.Fields.Count == 0,
                Attribute = attribute,
                Description = operation.Description ?? $"{operation.Name} operation",
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
                Fields = operation.InputType.Fields.Select(MapField).ToList(),
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
                Fields = operation.OutputType.Fields.Select(MapField).ToList(),
                HasFields = operation.OutputType.Fields.Count > 0,
            }
        );
    }

    public string RenderJunction(ApiOperation operation, string projectName)
    {
        var isUnit = IsUnitOutput(operation.OutputType);
        var ns = $"{projectName}.Trains.{operation.Group}.{operation.Name}";
        return Render(
            "Junction",
            new
            {
                Namespace = ns,
                TrainName = operation.Name,
                InputTypeName = operation.InputType.Fields.Count > 0
                    ? operation.InputType.Name
                    : "Unit",
                OutputTypeName = isUnit ? "Unit" : operation.OutputType.Name,
                OutputIsUnit = isUnit,
                InputIsUnit = operation.InputType.Fields.Count == 0,
                HttpMethod = operation.HttpMethod,
                HttpPath = operation.HttpPath,
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
                Values = apiEnum.Values,
                Description = apiEnum.Description,
            }
        );
    }

    public string RenderTrainsCsproj()
    {
        return Render("TrainsCsproj", new { });
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
            ["Description"] = field.Description,
            ["RequiredKeyword"] = field.IsRequired ? "required " : "",
            ["NullableMarker"] = field.IsNullable && !field.TypeName.EndsWith('?') ? "?" : "",
        };
        return obj;
    }

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
}
