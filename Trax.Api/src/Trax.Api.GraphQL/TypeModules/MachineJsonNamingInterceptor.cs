using System.Reflection;
using System.Text.Json.Serialization;
using HotChocolate.Configuration;
using HotChocolate.Types.Descriptors.Configurations;
using Trax.Effect.StateMachine.Persistence;

namespace Trax.Api.GraphQL.TypeModules;

/// <summary>
/// Names a field of a machine's input or context record by its <c>[JsonPropertyName]</c>, which HotChocolate does not
/// read, so a typed client sends and reads each field under the name the machine's IR, its twin and its stored context
/// use. A field without the attribute keeps HotChocolate's camelCase name, which is the one the server's camelCase
/// serializer writes. The records are each exposed machine's start and event inputs and its states' contexts, and the
/// records they hold; a record a train shares with a machine is renamed for the train too.
/// </summary>
internal sealed class MachineJsonNamingInterceptor(
    IEnumerable<IMachine> machines,
    StateMachineOptions? options
) : TypeInterceptor
{
    private HashSet<Type>? _records;

    private HashSet<Type> Records => _records ??= Collect();

    public override void OnBeforeCompleteName(
        ITypeCompletionContext completionContext,
        TypeSystemConfiguration configuration
    )
    {
        switch (configuration)
        {
            case InputObjectTypeConfiguration input when Records.Contains(input.RuntimeType):
                foreach (var field in input.Fields)
                    if (field.Property?.GetCustomAttribute<JsonPropertyNameAttribute>() is { } json)
                        field.Name = json.Name;
                break;
            case ObjectTypeConfiguration output when Records.Contains(output.RuntimeType):
                foreach (var field in output.Fields)
                    if (field.Member?.GetCustomAttribute<JsonPropertyNameAttribute>() is { } json)
                        field.Name = json.Name;
                break;
        }
    }

    private HashSet<Type> Collect()
    {
        var records = new HashSet<Type>();
        foreach (var machine in MachineSurface.Exposed(machines, options))
        {
            if (machine.StartInputType is { } start)
                Add(records, start);
            foreach (var trigger in MachineSurface.ClientTriggers(machine))
                if (trigger.InputType is { } input)
                    Add(records, input);
            foreach (var state in machine.States)
                if (state.ContextType is { } context)
                    Add(records, context);
        }
        return records;
    }

    // A record, and every record its properties hold, directly or as the elements of a collection.
    private static void Add(HashSet<Type> records, Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type.IsArray)
        {
            Add(records, type.GetElementType()!);
            return;
        }
        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments())
                Add(records, argument);
            if (type.Namespace?.StartsWith("System", StringComparison.Ordinal) == true)
                return;
        }
        if (
            type.IsPrimitive
            || type.IsEnum
            || type == typeof(string)
            || type.Namespace?.StartsWith("System", StringComparison.Ordinal) == true
            || !records.Add(type)
        )
            return;
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            Add(records, property.PropertyType);
    }
}
