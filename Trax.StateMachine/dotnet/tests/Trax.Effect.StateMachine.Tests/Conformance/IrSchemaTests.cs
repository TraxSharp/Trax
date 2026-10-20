using System.Reflection;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.Effect.StateMachine.Tests.Helpers;

namespace Trax.Effect.StateMachine.Tests.Conformance;

/// <summary>
/// <c>conformance/ir.schema.json</c> is the IR's normative shape, which an engine in any language is written against.
/// Every IR this exporter writes for the machines in this test assembly must validate, and the schema's objects are
/// closed, so a field the exporter starts writing fails here until the schema, and the IR format page, name it.
/// </summary>
public class IrSchemaTests
{
    [Test]
    public void Every_IR_the_exporter_writes_for_the_test_machines_validates()
    {
        var irs = ExportedIrs();
        irs.Should().NotBeEmpty();

        var results = TwinEngine.ValidateIrs(irs.Select(i => i.Ir).ToList());

        var invalid = irs.Zip(results)
            .Where(r => r.Second.Count > 0)
            .Select(r => $"{r.First.Name}:\n  " + string.Join("\n  ", r.Second))
            .ToList();
        invalid
            .Should()
            .BeEmpty(
                "every exported IR matches conformance/ir.schema.json:\n"
                    + string.Join("\n", invalid)
            );
    }

    [Test]
    public void The_test_machines_reach_every_optional_section()
    {
        var irs = ExportedIrs().Select(i => i.Ir).ToList();

        foreach (var section in new[] { "invariants", "outcomes", "timers", "differential" })
            irs.Should().Contain(ir => ir.ContainsKey(section), $"some test machine has {section}");
        irs.Should()
            .Contain(
                ir => ir["transitions"]!.AsArray().Any(t => t!["effect"] != null),
                "some test machine has an effect-bound edge"
            );
    }

    private static List<(string Name, JsonObject Ir)> ExportedIrs()
    {
        var export = typeof(IrExporter).GetMethod(nameof(IrExporter.Export))!;
        var refuses = typeof(IrExporter).GetMethod(
            nameof(IrExporter.RefusesByDesign),
            BindingFlags.NonPublic | BindingFlags.Static
        )!;
        const BindingFlags statics =
            BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.Static
            | BindingFlags.DeclaredOnly;

        var irs = new List<(string, JsonObject)>();
        foreach (
            var type in typeof(IrSchemaTests)
                .Assembly.GetTypes()
                .Where(t => !t.ContainsGenericParameters)
                .OrderBy(t => t.FullName, StringComparer.Ordinal)
        )
        foreach (
            var (name, read) in type.GetFields(statics)
                .Where(f => IsBuiltMachine(f.FieldType))
                .Select(f => (f.Name, (Func<object?>)(() => f.GetValue(null))))
                .Concat(
                    type.GetProperties(statics)
                        .Where(p =>
                            IsBuiltMachine(p.PropertyType) && p.GetIndexParameters().Length == 0
                        )
                        .Select(p => (p.Name, (Func<object?>)(() => p.GetValue(null))))
                )
        )
        {
            if (read() is not { } machine)
                continue;
            var args = machine.GetType().GetGenericArguments();
            if ((bool)refuses.MakeGenericMethod(args).Invoke(null, [machine])!)
                continue;
            var ir = (string)export.MakeGenericMethod(args).Invoke(null, [machine])!;
            irs.Add(($"{type.FullName}.{name}", (JsonObject)JsonNode.Parse(ir)!));
        }
        return irs;
    }

    private static bool IsBuiltMachine(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(BuiltMachine<,>);
}
