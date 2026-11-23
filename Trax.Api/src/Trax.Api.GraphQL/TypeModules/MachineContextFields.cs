using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using HotChocolate;
using HotChocolate.Authorization;
using HotChocolate.Execution.Configuration;
using HotChocolate.Language;
using HotChocolate.Types;
using HotChocolate.Types.Descriptors;
using Trax.Api.GraphQL.Validation;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Api.GraphQL.TypeModules;

/// <summary>The fields of a machine's context type, from the records its states declare.</summary>
internal static class MachineContextFields
{
    /// <summary>
    /// Every field any state's context record declares, by JSON name, in the order the states and the records declare
    /// them. Two records giving one name different types refuse the machine, naming both.
    /// </summary>
    public static IReadOnlyList<MachineContextField> Of(
        IMachine machine,
        IReadOnlyList<MachineState> states
    )
    {
        var fields = new List<MachineContextField>();
        var owners = new Dictionary<string, (Type Record, Type Field)>(StringComparer.Ordinal);
        foreach (var record in states.Select(s => s.ContextType).OfType<Type>().Distinct())
        foreach (var property in record.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0 || property.PropertyType == typeof(object))
                continue;
            var name = MachineSurface.JsonName(property);
            if (owners.TryGetValue(name, out var owner))
            {
                if (owner.Field != property.PropertyType)
                    throw new InvalidOperationException(
                        $"The machine '{machine.Name}' declares the context field '{name}' as "
                            + $"{owner.Field.Name} in {owner.Record.Name} and as {property.PropertyType.Name} in "
                            + $"{record.Name}. Its typed context has one field per name: give the field one type."
                    );
                continue;
            }
            owners[name] = (record, property.PropertyType);
            fields.Add(new MachineContextField(name, property.PropertyType));
        }
        return fields;
    }
}
