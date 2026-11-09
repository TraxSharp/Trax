using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Trax.StateMachine.Persistence.FromIr;

/// <summary>
/// The CLR types a machine served from its IR needs, emitted into a dynamic assembly of its own: the state and trigger
/// enums its <see cref="Machine{TState,TTrigger}"/> is closed over, and a record per declared context, trigger input and
/// start, which the API types its operations from. The records carry data only, a property per field with its JSON name;
/// the engines check every value against the IR's own schema, never against these (ADR <c>Trax.Docs/adr/0058</c>).
/// </summary>
/// <remarks>
/// Every name emitted comes from the IR, so each is checked before it is used: states and triggers are
/// <c>[A-Z][A-Za-z0-9]*</c>, a field is a C# identifier once its first letter is upper-cased, and two fields of one
/// record may not upper-case to the same name. Nothing else from the IR reaches the assembly but the JSON names, as
/// attribute strings. No method body is emitted but a property's get and set.
/// </remarks>
internal sealed partial class IrTypes
{
    private readonly ModuleBuilder _module;
    private readonly string _namespace;
    private readonly HashSet<string> _emitted = new(StringComparer.Ordinal);

    private IrTypes(string machineId)
    {
        var pascal = Pascal(machineId);
        _namespace = $"Trax.Machines.FromIr.{pascal}";
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName($"Trax.Machines.FromIr.{pascal}.{Guid.NewGuid():N}"),
            AssemblyBuilderAccess.Run
        );
        _module = assembly.DefineDynamicModule(_namespace);
        Prefix = pascal;
    }

    /// <summary>The machine id in Pascal case, which every emitted type name starts with.</summary>
    internal string Prefix { get; }

    internal Type State { get; private set; } = null!;
    internal Type Trigger { get; private set; } = null!;

    /// <summary>Each declared state's context record, by state.</summary>
    internal Dictionary<string, Type> Contexts { get; } = new(StringComparer.Ordinal);

    /// <summary>Each declared trigger's input record, by trigger.</summary>
    internal Dictionary<string, Type> Inputs { get; } = new(StringComparer.Ordinal);

    /// <summary>The record a client starts an instance with: no fields, since an IR declares no start input.</summary>
    internal Type Start { get; private set; } = null!;

    /// <summary>Emits every type the machine needs.</summary>
    /// <exception cref="InvalidOperationException">A name in the IR cannot be emitted; the message names it.</exception>
    internal static IrTypes Emit(
        string machineId,
        IReadOnlyList<string> states,
        IReadOnlyList<string> triggers,
        IReadOnlyDictionary<string, ContextSchema> contexts,
        IReadOnlyDictionary<string, ContextSchema> inputs
    )
    {
        var types = new IrTypes(machineId);
        types.State = types.EmitEnum("State", states, "state");
        types.Trigger = types.EmitEnum("Trigger", triggers, "trigger");
        foreach (var (state, schema) in contexts)
            types.Contexts[state] = types.EmitRecord($"{state}Context", schema.Fields);
        foreach (var (trigger, schema) in inputs)
            types.Inputs[trigger] = types.EmitRecord($"{trigger}Input", schema.Fields);
        types.Start = types.EmitRecord("StartInput", []);
        return types;
    }

    private Type EmitEnum(string suffix, IReadOnlyList<string> members, string what)
    {
        var builder = _module.DefineEnum(Name(suffix), TypeAttributes.Public, typeof(int));
        for (var i = 0; i < members.Count; i++)
        {
            if (!Member().IsMatch(members[i]))
                throw new InvalidOperationException(
                    $"The {what} '{members[i]}' is not a name a machine can have: a capital letter, then letters and "
                        + "digits."
                );
            builder.DefineLiteral(members[i], i);
        }
        return builder.CreateType();
    }

    private Type EmitRecord(string suffix, IReadOnlyList<FieldSchema> fields)
    {
        var type = _module.DefineType(
            Name(suffix),
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class
        );
        type.DefineDefaultConstructor(MethodAttributes.Public);

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            var property = PropertyName(field.Name, suffix);
            if (!names.Add(property))
                throw new InvalidOperationException(
                    $"{suffix} has two fields that are both the property '{property}' in C#; rename one."
                );
            DefineProperty(type, property, field.Name, ClrType(field, $"{suffix}{property}"));
        }
        return type.CreateType();
    }

    // A field's type, as the API shows it. A record or a list of records gets a record of its own.
    private Type ClrType(FieldSchema field, string owner)
    {
        Type type = field.Type switch
        {
            JsonFieldType.String => field.Format switch
            {
                "uuid" => typeof(Guid),
                "date-time" => typeof(DateTime),
                "date-time-offset" => typeof(DateTimeOffset),
                "date" => typeof(DateOnly),
                "time" => typeof(TimeOnly),
                // A duration is always nullable, as a timer's field must be (a TimeSpan? read from the context).
                "duration" => typeof(TimeSpan?),
                "uri" => typeof(Uri),
                "char" => typeof(char),
                _ => typeof(string),
            },
            JsonFieldType.Integer => typeof(long),
            JsonFieldType.Number => typeof(double),
            JsonFieldType.Boolean => typeof(bool),
            JsonFieldType.Array => typeof(List<>).MakeGenericType(Element(field, owner)),
            JsonFieldType.Object when field.Values is { } values =>
                typeof(Dictionary<,>).MakeGenericType(
                    typeof(string),
                    ClrType(values, $"{owner}Value")
                ),
            JsonFieldType.Object => EmitRecord(owner, field.Fields ?? []),
            _ => throw new InvalidOperationException(
                $"'{field.Name}' has the unknown type {field.Type}."
            ),
        };
        return field.Nullable && type.IsValueType && Nullable.GetUnderlyingType(type) is null
            ? typeof(Nullable<>).MakeGenericType(type)
            : type;
    }

    private Type Element(FieldSchema list, string owner) =>
        list.Fields is { } fields ? EmitRecord($"{owner}Item", fields)
        : list.Values is { } values ? ClrType(values, $"{owner}Item")
        : typeof(System.Text.Json.Nodes.JsonNode);

    private static void DefineProperty(TypeBuilder type, string name, string jsonName, Type clr)
    {
        var field = type.DefineField(
            $"_{char.ToLowerInvariant(name[0])}{name[1..]}",
            clr,
            FieldAttributes.Private
        );
        var property = type.DefineProperty(name, PropertyAttributes.None, clr, null);
        const MethodAttributes accessor =
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig;

        var get = type.DefineMethod($"get_{name}", accessor, clr, Type.EmptyTypes);
        var il = get.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, field);
        il.Emit(OpCodes.Ret);
        property.SetGetMethod(get);

        var set = type.DefineMethod($"set_{name}", accessor, null, [clr]);
        il = set.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, field);
        il.Emit(OpCodes.Ret);
        property.SetSetMethod(set);

        property.SetCustomAttribute(
            new CustomAttributeBuilder(
                typeof(JsonPropertyNameAttribute).GetConstructor([typeof(string)])!,
                [jsonName]
            )
        );
    }

    private string Name(string suffix)
    {
        var name = $"{_namespace}.{Prefix}{suffix}";
        if (!_emitted.Add(name))
            throw new InvalidOperationException(
                $"Two types of the machine would both be named {name}."
            );
        return name;
    }

    /// <summary>A field's property name: its JSON name with the first letter upper-cased, which must be a C# identifier.</summary>
    internal static string PropertyName(string field, string owner)
    {
        if (!Identifier().IsMatch(field))
            throw new InvalidOperationException(
                $"{owner} has the field '{field}', which is not a name a property can have: a letter or '_', then "
                    + "letters, digits and '_'."
            );
        return char.ToUpperInvariant(field[0]) + field[1..];
    }

    /// <summary>A kebab-case machine id in Pascal case: <c>turnstile-two</c> is <c>TurnstileTwo</c>.</summary>
    internal static string Pascal(string id) =>
        string.Concat(
            id.Split('-')
                .Select(word => word.Length == 0 ? "" : char.ToUpperInvariant(word[0]) + word[1..])
        );

    [GeneratedRegex(@"\A[A-Z][A-Za-z0-9]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex Member();

    [GeneratedRegex(@"\A[A-Za-z_][A-Za-z0-9_]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex Identifier();
}
