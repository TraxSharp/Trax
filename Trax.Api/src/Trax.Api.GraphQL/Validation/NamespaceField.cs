using System.Reflection;
using HotChocolate.Features;
using HotChocolate.Types;
using HotChocolate.Types.Descriptors;

namespace Trax.Api.GraphQL.Validation;

/// <summary>
/// Marks a field that groups other fields rather than doing work of its own: <c>dispatch</c>,
/// <c>discover</c>, <c>operations</c>, their nested namespaces such as
/// <c>operations { deadLetters }</c>, and the namespaces trains and query models declare.
/// <see cref="OperationCountValidatorRule"/> counts the selections under a marked field instead
/// of the field itself, so the per-request cap applies to the train and admin fields a request
/// actually invokes.
/// </summary>
internal sealed class NamespaceField
{
    private static readonly NamespaceField Marker = new();

    private NamespaceField() { }

    /// <summary>Marks a field built through a descriptor as a namespace.</summary>
    internal static IObjectFieldDescriptor Mark(IObjectFieldDescriptor descriptor)
    {
        descriptor.Extend().Configuration.Features.Set(Marker);
        return descriptor;
    }

    /// <summary>True when <paramref name="field"/> was marked as a namespace.</summary>
    internal static bool IsNamespace(IOutputFieldDefinition field) =>
        field is IFeatureProvider provider && provider.Features.Get<NamespaceField>() is not null;
}

/// <summary>
/// Marks a resolver method that returns a nested namespace, such as
/// <c>OperationsQueries.DeadLetters()</c>. See <see cref="NamespaceField"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property)]
internal sealed class NamespaceFieldAttribute : ObjectFieldDescriptorAttribute
{
    protected override void OnConfigure(
        IDescriptorContext context,
        IObjectFieldDescriptor descriptor,
        MemberInfo? member
    ) => NamespaceField.Mark(descriptor);
}
