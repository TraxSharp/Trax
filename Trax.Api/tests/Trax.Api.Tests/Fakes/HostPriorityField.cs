using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Api.Tests.Fakes.Machines;

/// <summary>A host's root query field returning <see cref="ExtendedPriority"/>.</summary>
public sealed class HostPriorityField : HotChocolate.Types.ObjectTypeExtension
{
    protected override void Configure(HotChocolate.Types.IObjectTypeDescriptor descriptor)
    {
        descriptor.Name("RootQuery");
        descriptor.Field<HostPriorityResolvers>(r => r.HostPriority());
    }
}
