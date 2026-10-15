using HotChocolate.Configuration;
using HotChocolate.Language;
using HotChocolate.Types;
using HotChocolate.Types.Descriptors.Configurations;

namespace Trax.Api.GraphQL.TypeModules;

/// <summary>
/// Refuses a schema where a field outside the machines root carries an enum a served machine spells as the server
/// stores it, unless the host accepted that with <c>ShareEnumWithMachines</c>. HotChocolate binds one GraphQL type to
/// a CLR enum, so the moment a machine holds one, every field that carries it changes from <c>UPPER_SNAKE</c> to the
/// member names: a train's, a query model's, a host's type extension's. This reads the built schema, so it sees every
/// one of them; <see cref="MachineEnums.SharingProblems"/> names the train or query model earlier, from their CLR
/// types. An enum of Trax's own, which Trax's fields carry, is refused before either
/// (<see cref="MachineEnums.TraxEnumProblems"/>). See
/// <c>Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md</c>.
/// </summary>
internal sealed class MachineEnumSharingInterceptor(IReadOnlySet<Type> accepted) : TypeInterceptor
{
    private readonly List<ObjectType> _roots = [];
    private readonly HashSet<string> _pushFields = new(StringComparer.Ordinal);
    private readonly List<ObjectType> _objects = [];

    public override void OnAfterCompleteName(
        ITypeCompletionContext completionContext,
        TypeSystemConfiguration configuration
    )
    {
        if (
            completionContext.Type is MachinePushExtension
            && configuration is ObjectTypeConfiguration push
        )
            foreach (var field in push.Fields)
                _pushFields.Add(field.Name);
    }

    public override void OnAfterResolveRootType(
        ITypeCompletionContext completionContext,
        ObjectTypeConfiguration configuration,
        OperationType operationType
    )
    {
        if (completionContext.Type is ObjectType root)
            _roots.Add(root);
    }

    public override void OnAfterCompleteType(
        ITypeCompletionContext completionContext,
        TypeSystemConfiguration configuration
    )
    {
        if (completionContext.Type is ObjectType type)
            _objects.Add(type);
    }

    public override void OnAfterCompleteTypes()
    {
        var problems = Problems();
        if (problems.Count > 0)
            throw new InvalidOperationException(string.Join(Environment.NewLine, problems));
    }

    // Every field, argument and input field reachable from a root field other than the machines root and the machines'
    // push fields, whose type is a machine enum the host did not accept.
    private List<string> Problems()
    {
        var problems = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<(ITypeDefinition Type, string From)>();

        foreach (var root in _roots)
        foreach (var field in root.Fields)
        {
            if (field.IsIntrospectionField || IsMachines(root.Name, field.Name))
                continue;
            var from = $"{root.Name}.{field.Name}";
            Carry(problems, $"{root.Name}.{field.Name}", field.Type, from, queue);
            foreach (var argument in field.Arguments)
                Carry(
                    problems,
                    $"{root.Name}.{field.Name}({argument.Name}:)",
                    argument.Type,
                    from,
                    queue
                );
        }

        while (queue.Count > 0)
        {
            var (type, from) = queue.Dequeue();
            if (!seen.Add(type.Name))
                continue;
            switch (type)
            {
                case IComplexTypeDefinition complex:
                    foreach (var field in complex.Fields)
                    {
                        if (field.IsIntrospectionField)
                            continue;
                        Carry(problems, $"{type.Name}.{field.Name}", field.Type, from, queue);
                        foreach (var argument in field.Arguments)
                            Carry(
                                problems,
                                $"{type.Name}.{field.Name}({argument.Name}:)",
                                argument.Type,
                                from,
                                queue
                            );
                    }
                    if (type is InterfaceType)
                        foreach (var implementation in _objects)
                            if (implementation.Implements.Any(i => i.Name == type.Name))
                                queue.Enqueue((implementation, from));
                    break;
                case UnionType union:
                    foreach (var member in union.Types)
                        queue.Enqueue((member, from));
                    break;
                case InputObjectType input:
                    foreach (var field in input.Fields)
                        Carry(problems, $"{type.Name}.{field.Name}", field.Type, from, queue);
                    break;
            }
        }

        return problems.Distinct(StringComparer.Ordinal).ToList();
    }

    private bool IsMachines(string root, string field) =>
        (root, field) switch
        {
            (_, MachineSurface.RootField) => true,
            (MachineChangedSubscription.SubscriptionType, var name) => _pushFields.Contains(name),
            _ => false,
        };

    private void Carry(
        List<string> problems,
        string field,
        IType type,
        string from,
        Queue<(ITypeDefinition, string)> queue
    )
    {
        var named = type.NamedType();
        if (MachineEnumOf(named) is { } held)
        {
            if (!accepted.Contains(held))
                problems.Add(Problem(held, field, from));
            return;
        }
        queue.Enqueue((named, from));
    }

    private static Type? MachineEnumOf(ITypeDefinition type) =>
        type.GetType() is { IsGenericType: true } clr
        && clr.GetGenericTypeDefinition() == typeof(MachineEnumType<>)
            ? clr.GetGenericArguments()[0]
            : null;

    private static string Problem(Type held, string field, string from) =>
        $"The enum {held.FullName} is held by a served machine, which spells its values as the server stores them, "
        + $"and the field '{field}'"
        + (field.StartsWith(from, StringComparison.Ordinal) ? "" : $" (reached from '{from}')")
        + " carries it too: GraphQL has one type per enum, so that field's wire would change from upper snake case "
        + "to the member names. Give the machine its own enum, or call "
        + $"ShareEnumWithMachines<{held.Name}>() on AddTraxGraphQL to accept the new spelling there and update that "
        + "field's clients.";
}
