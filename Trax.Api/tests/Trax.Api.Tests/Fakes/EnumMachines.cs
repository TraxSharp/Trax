using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.Effect.StateMachine;
using Trax.Effect.StateMachine.Persistence;

namespace Trax.Api.Tests.Fakes.Machines
{
    // The machines of the enum spelling tests: each holds one enum in its context, so the schema spells that enum as
    // the server stores it.

    public enum HeldState
    {
        Open,
        Done,
    }

    public enum HeldTrigger
    {
        Close,
    }

    public sealed record HeldContext<TValue>
    {
        public TValue? Value { get; init; }
    }

    /// <summary>A machine whose context holds a <typeparamref name="TValue"/>, under the id the subclass gives.</summary>
    public abstract class HoldingMachine<TValue>(string id) : Machine<HeldState, HeldTrigger>
    {
        protected override void Configure(IMachineBuilder<HeldState, HeldTrigger> m)
        {
            m.Id(id).Version(1).StartsAt(HeldState.Open, () => new JsonObject());
            m.Start<StepStartInput>(s => s.AnyAuthenticated());
            m.Get(r => r.OwnerOnly());
            m.Watch(r => r.OwnerOnly());
            m.Discard(r => r.OwnerOnly());
            m.In(HeldState.Open)
                .Context<HeldContext<TValue>>()
                .On(HeldTrigger.Close)
                .AnyAuthenticated()
                .To(HeldState.Done);
            m.In(HeldState.Done).Context<HeldContext<TValue>>();
        }
    }

    /// <summary>A member C# spells <c>@null</c>, which the server stores as <c>null</c>, a name GraphQL reserves.</summary>
    public enum NullMember
    {
        Open,
        @null,
    }

    [TraxMachine]
    public sealed class NullMemberMachine() : HoldingMachine<NullMember>("null-member")
    {
        public const string MachineId = "null-member";
    }

    /// <summary>A member starting with two underscores, which GraphQL reserves for introspection.</summary>
    public enum DunderMember
    {
        Open,
        __Hidden,
    }

    [TraxMachine]
    public sealed class DunderMemberMachine() : HoldingMachine<DunderMember>("dunder-member")
    {
        public const string MachineId = "dunder-member";
    }

    /// <summary>An enum a machine and a GraphQL train both reach.</summary>
    public enum SharedPriority
    {
        Low,
        VeryHigh,
    }

    [TraxMachine]
    public sealed class SharedPriorityMachine() : HoldingMachine<SharedPriority>("shared-priority")
    {
        public const string MachineId = "shared-priority";
    }

    public sealed record SharedPriorityInput
    {
        public SharedPriority Priority { get; init; }
    }

    /// <summary>The service a query train taking <see cref="SharedPriorityInput"/> is registered under.</summary>
    public interface ISharedPriorityTrain;

    /// <summary>An enum the host binds a GraphQL type to itself.</summary>
    public enum HostBound
    {
        Low,
        VeryHigh,
    }

    [TraxMachine]
    public sealed class HostBoundMachine() : HoldingMachine<HostBound>("host-bound")
    {
        public const string MachineId = "host-bound";
    }

    /// <summary>A machine holding Trax's own <see cref="Trax.Effect.Enums.TrainState"/>, which Trax's subscription carries.</summary>
    [TraxMachine]
    public sealed class TrainStateHoldingMachine()
        : HoldingMachine<Trax.Effect.Enums.TrainState>("train-state-holding")
    {
        public const string MachineId = "train-state-holding";
    }

    /// <summary>An enum a machine holds only through a class deriving from a list.</summary>
    public enum ListedPriority
    {
        Low,
        VeryHigh,
    }

    public sealed class ListedPriorities : List<ListedPriority>;

    [TraxMachine]
    public sealed class ListedPriorityMachine()
        : HoldingMachine<ListedPriorities>("listed-priority")
    {
        public const string MachineId = "listed-priority";
    }

    /// <summary>An enum a machine holds and a host's own type extension returns.</summary>
    public enum ExtendedPriority
    {
        Low,
        VeryHigh,
    }

    [TraxMachine]
    public sealed class ExtendedPriorityMachine()
        : HoldingMachine<ExtendedPriority>("extended-priority")
    {
        public const string MachineId = "extended-priority";
    }

    /// <summary>A host's root query field returning <see cref="ExtendedPriority"/>.</summary>
    public sealed class HostPriorityField : HotChocolate.Types.ObjectTypeExtension
    {
        protected override void Configure(HotChocolate.Types.IObjectTypeDescriptor descriptor)
        {
            descriptor.Name("RootQuery");
            descriptor.Field<HostPriorityResolvers>(r => r.HostPriority());
        }
    }

    public sealed class HostPriorityResolvers
    {
        [TraxAllowAnonymous]
        public ExtendedPriority HostPriority() => ExtendedPriority.Low;
    }

    /// <summary>Enums a record holds only inside framework generics.</summary>
    public enum PairedPriority
    {
        Low,
    }

    public enum TupledPriority
    {
        Low,
    }

    public enum ValueTupledPriority
    {
        Low,
    }

    public sealed record FrameworkGenerics
    {
        public Dictionary<string, PairedPriority> Pairs { get; init; } = [];
        public Tuple<string, TupledPriority>? Tuple { get; init; }
        public (int, ValueTupledPriority) ValueTuple { get; init; }
    }

    /// <summary>A machine whose push field is <c>onTrainStateChanged</c>, which Trax's own subscription already is.</summary>
    [TraxMachine]
    public sealed class OnTrainStateMachine() : NamedStepMachine("on-train-state")
    {
        public const string MachineId = "on-train-state";
    }

    /// <summary>A machine whose push field is <c>orderChanged</c>.</summary>
    [TraxMachine]
    public sealed class OrderStepMachine() : NamedStepMachine("order")
    {
        public const string MachineId = "order";
    }
}

namespace Systematic.Orders
{
    /// <summary>A consumer namespace that starts with <c>System</c> without being it.</summary>
    public enum Grade
    {
        Low,
        VeryHigh,
    }

    public sealed record Graded
    {
        public Grade Grade { get; init; }
    }

    [TraxMachine]
    public sealed class GradedMachine()
        : Trax.Api.Tests.Fakes.Machines.HoldingMachine<Graded>("graded")
    {
        public const string MachineId = "graded";
    }
}
