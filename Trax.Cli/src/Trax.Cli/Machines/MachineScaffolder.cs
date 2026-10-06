using System.Reflection;
using Scriban;
using Scriban.Runtime;
using Trax.Cli.Generator;

namespace Trax.Cli.Machines;

/// <summary>
/// Scaffolds a new Tier-1 machine: one C# file authored with the declarative surface (states, triggers, a
/// context, one guarded transition, and the differential wiring), optionally with an exactly-once effect stub.
/// The output uses the same idioms as a real machine, so `trax machine generate` runs on it as-is.
/// </summary>
internal static class MachineScaffolder
{
    private static readonly Template Template = LoadTemplate();

    /// <summary>Render the machine source for <paramref name="name"/> (kebab id -> PascalCase types).</summary>
    internal static string Render(string name, string @namespace, bool withEffect)
    {
        var id = Kebab(name);
        var model = new
        {
            Id = id,
            Pascal = Pascal(id),
            Namespace = @namespace,
            WithEffect = withEffect,
        };

        var scriptObject = new ScriptObject();
        scriptObject.Import(model, renamer: member => member.Name);
        var context = new TemplateContext();
        context.PushGlobal(scriptObject);
        return Template.Render(context);
    }

    /// <summary>Write the scaffold to <c>&lt;outputDir&gt;/&lt;Pascal&gt;Machine.cs</c> and return the path.</summary>
    internal static string Write(
        string name,
        string outputDir,
        string @namespace,
        bool withEffect,
        bool force
    )
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException(
                "A machine name is required, e.g. 'trax machine new checkout'."
            );

        var pascal = Pascal(Kebab(name));
        // The name becomes type names and the machine id literal, the namespace a declaration.
        if (!SchemaNames.IsIdentifier(pascal))
            throw new InvalidOperationException(
                $"The machine name '{name}' does not make a C# identifier: use letters, digits, "
                    + "'-', '_' or spaces, starting with a letter, e.g. 'write-to-congress'."
            );
        if (!SchemaNames.IsDottedIdentifier(@namespace))
            throw new InvalidOperationException(
                $"The namespace '{@namespace}' is not a C# namespace: use identifiers matching "
                    + $"{SchemaNames.Pattern} joined by dots."
            );

        Directory.CreateDirectory(outputDir);
        var path = Path.Combine(outputDir, $"{pascal}Machine.cs");
        if (File.Exists(path) && !force)
            throw new InvalidOperationException(
                $"{path} already exists. Use --force to overwrite."
            );

        File.WriteAllText(path, Render(name, @namespace, withEffect));
        return path;
    }

    // A machine id is kebab-case (write-to-congress); the type prefix is PascalCase (WriteToCongress).
    internal static string Kebab(string name) =>
        string.Join('-', SplitWords(name).Select(w => w.ToLowerInvariant()));

    internal static string Pascal(string name) =>
        string.Concat(
            SplitWords(name).Select(w => char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant())
        );

    private static IEnumerable<string> SplitWords(string name) =>
        name.Split(['-', '_', ' '], StringSplitOptions.RemoveEmptyEntries);

    private static Template LoadTemplate()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream =
            assembly.GetManifestResourceStream("Trax.Cli.Templates.MachineScaffold.sbn")
            ?? throw new InvalidOperationException(
                "Embedded template MachineScaffold.sbn not found."
            );
        using var reader = new StreamReader(stream);
        return Template.Parse(reader.ReadToEnd(), "MachineScaffold.sbn");
    }
}
