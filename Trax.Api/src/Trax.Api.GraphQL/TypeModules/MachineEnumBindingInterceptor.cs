using HotChocolate.Configuration;
using HotChocolate.Types;
using HotChocolate.Types.Descriptors.Configurations;

namespace Trax.Api.GraphQL.TypeModules;

/// <summary>
/// Refuses a host that binds its own GraphQL enum type to a CLR enum a served machine holds. Trax binds that enum to
/// the type that spells its values as the server stores them (<see cref="MachineEnumType{TEnum}"/>), and HotChocolate
/// takes one type per CLR enum, so two would fail the schema in its words, or leave the machine's twin reading a
/// spelling the server never writes. See
/// <c>Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md</c>.
/// </summary>
internal sealed class MachineEnumBindingInterceptor : TypeInterceptor
{
    private readonly HashSet<Type> _machine = [];
    private readonly Dictionary<Type, List<Type>> _host = [];

    public override void OnAfterInitialize(
        ITypeDiscoveryContext discoveryContext,
        TypeSystemConfiguration configuration
    )
    {
        if (
            discoveryContext.Type is not EnumType type
            || configuration is not EnumTypeConfiguration { RuntimeType: { IsEnum: true } clr }
        )
            return;

        if (IsMachineEnumType(type.GetType()))
        {
            _machine.Add(clr);
            return;
        }
        if (!_host.TryGetValue(clr, out var types))
            _host[clr] = types = [];
        types.Add(type.GetType());
    }

    public override void OnAfterDiscoverTypes()
    {
        var problems = _host
            .Where(h => _machine.Contains(h.Key))
            .OrderBy(h => h.Key.FullName, StringComparer.Ordinal)
            .Select(h =>
                $"The enum {h.Key.FullName} is held by a served machine, so Trax binds it to a GraphQL type that "
                + "spells its values as the server stores them, but the host binds it to "
                + string.Join(" and ", h.Value.Select(t => t.FullName))
                + " as well. Remove the host's type, or give the machine its own enum."
            )
            .ToList();
        if (problems.Count > 0)
            throw new InvalidOperationException(string.Join(Environment.NewLine, problems));
    }

    private static bool IsMachineEnumType(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(MachineEnumType<>);
}
