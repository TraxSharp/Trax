using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using HotChocolate.Types;
using Trax.Core.Extensions;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Api.GraphQL.TypeModules;

/// <summary>
/// A machine enum's GraphQL type: its values named as the server stores them. Its own class so a type the host binds
/// to the same CLR enum can be told apart from it (<see cref="MachineEnumBindingInterceptor"/>).
/// </summary>
internal sealed class MachineEnumType<TEnum>() : EnumType<TEnum>(Describe)
    where TEnum : struct, Enum
{
    private static void Describe(IEnumTypeDescriptor<TEnum> d)
    {
        d.BindValuesExplicitly();
        foreach (var value in Enum.GetValues<TEnum>())
        {
            var name = JsonSerializer
                .Deserialize<string>(JsonSerializer.Serialize(value, MachineSurface.Json))
                .NotNull();
            if (MachineEnums.ValueNameProblem(typeof(TEnum), name) is { } problem)
                throw new InvalidOperationException(problem);
            d.Value(value).Name(name);
        }
    }
}
