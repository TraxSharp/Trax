using HotChocolate;
using HotChocolate.Features;
using HotChocolate.Language;
using HotChocolate.Types;
using HotChocolate.Validation;

namespace Trax.Api.GraphQL.Validation;

/// <summary>
/// Marks a field under the <c>machines</c> root that writes an instance: <c>start</c>, <c>discard</c> and every
/// event. <see cref="MachineEventCountValidatorRule"/> refuses an operation that selects more than one.
/// </summary>
internal sealed class MachineWriteField
{
    private static readonly MachineWriteField Marker = new();

    private MachineWriteField() { }

    /// <summary>Marks a field built through a descriptor as a machine write.</summary>
    internal static IObjectFieldDescriptor Mark(IObjectFieldDescriptor descriptor)
    {
        descriptor.Extend().Configuration.Features.Set(Marker);
        return descriptor;
    }

    /// <summary>True when <paramref name="field"/> was marked as a machine write.</summary>
    internal static bool IsMachineWrite(IOutputFieldDefinition field) =>
        field is IFeatureProvider provider
        && provider.Features.Get<MachineWriteField>() is not null;
}
