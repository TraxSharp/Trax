using System.Reflection;
using HotChocolate.Features;
using HotChocolate.Types;
using HotChocolate.Types.Descriptors;

namespace Trax.Api.GraphQL.Validation;

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
