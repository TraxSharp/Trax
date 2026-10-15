using HotChocolate.Configuration;
using HotChocolate.Types;
using HotChocolate.Types.Descriptors.Configurations;
using Trax.Api.GraphQL.Subscriptions;

namespace Trax.Api.GraphQL.TypeModules;

/// <summary>
/// Refuses a machine whose push field (<c>&lt;machine&gt;Changed</c>) lands on a subscription field that is already
/// there: one of Trax's own (a machine <c>on-train-state</c> is <c>onTrainStateChanged</c>) or one the host adds.
/// HotChocolate would refuse the schema too, but in words that name neither the machine nor the fix. See
/// <c>Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md</c>.
/// </summary>
internal sealed class MachinePushFieldCollisionInterceptor : TypeInterceptor
{
    private readonly Dictionary<string, string> _pushes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _others = new(StringComparer.Ordinal);

    public override void OnAfterCompleteName(
        ITypeCompletionContext completionContext,
        TypeSystemConfiguration configuration
    )
    {
        if (configuration is not ObjectTypeConfiguration type || !OnSubscriptionRoot(type))
            return;

        foreach (var field in type.Fields)
        {
            if (field.Ignore)
                continue;
            if (completionContext.Type is MachinePushExtension push)
            {
                _pushes[field.Name] = push.MachineId;
                continue;
            }
            if (!_others.TryGetValue(field.Name, out var owners))
                _others[field.Name] = owners = [];
            owners.Add(Owner(completionContext.Type, field));
        }
    }

    public override void OnBeforeMergeTypeExtensions()
    {
        var problems = _pushes
            .Where(p => _others.ContainsKey(p.Key))
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p =>
                $"The machine '{p.Value}' is pushed on the subscription field '{p.Key}', which "
                + string.Join(" and ", _others[p.Key].Distinct())
                + " already puts there. Give the machine another id, or rename the other field."
            )
            .ToList();
        if (problems.Count > 0)
            throw new InvalidOperationException(string.Join(Environment.NewLine, problems));
    }

    private static bool OnSubscriptionRoot(ObjectTypeConfiguration type) =>
        type.RuntimeType == typeof(LifecycleSubscriptions)
        || type.ExtendsType == typeof(LifecycleSubscriptions)
        || type.Name == MachineChangedSubscription.SubscriptionType;

    private static string Owner(TypeSystemObject type, ObjectFieldConfiguration field)
    {
        if (type is not ObjectTypeExtension)
            return $"Trax's own '{field.Name}' subscription";
        if ((field.ResolverMember ?? field.Member) is { DeclaringType: { } declaring } member)
            return $"'{declaring.FullName}.{member.Name}'";
        return "a field a type extension builds in code";
    }
}
