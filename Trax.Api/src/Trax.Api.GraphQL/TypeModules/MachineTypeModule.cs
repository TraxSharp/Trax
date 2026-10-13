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
using Trax.Effect.StateMachine;
using Trax.Effect.StateMachine.Persistence;

namespace Trax.Api.GraphQL.TypeModules;

/// <summary>
/// Builds the <c>machines</c> root: for each registered machine marked <c>[TraxMachine]</c>,
/// <c>mutation { machines { &lt;machine&gt; { start discard &lt;event&gt;... } } }</c> and
/// <c>query { machines { &lt;machine&gt; { get } } }</c>, each answering with the server's snapshot of the instance,
/// its revision beside it, or a problem. Input types are the CLR records the machine declares
/// (<see cref="IMachine.Triggers"/>, <see cref="IMachine.StartInputType"/>), registered once however many machines
/// and trains take them; an event declared without <c>WithInput</c> takes no input argument, and an outcome, a timer
/// or an effect's receipt is never a client argument. Every operation field requires an authenticated caller; each
/// event's own rule is checked below, where every surface meets. See
/// <c>Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md</c>.
/// </summary>
internal sealed class MachineTypeModule(
    IEnumerable<IMachine> machines,
    StateMachineOptions? options = null
) : TypeModule
{
    public override ValueTask<IReadOnlyCollection<ITypeSystemMember>> CreateTypesAsync(
        IDescriptorContext context,
        CancellationToken cancellationToken
    )
    {
        var exposed = MachineSurface.Exposed(machines, options);
        var types = new List<ITypeSystemMember>();
        if (exposed.Count == 0)
            return new ValueTask<IReadOnlyCollection<ITypeSystemMember>>(types);

        var problems = MachineSurface.NameProblems(exposed);
        if (problems.Count > 0)
            throw new InvalidOperationException(
                $"{problems.Count} problem(s) stop this host from serving its machines under the machines root:"
                    + Environment.NewLine
                    + string.Join(Environment.NewLine, problems.Select(p => "  - " + p))
            );

        types.Add(NamespaceTypes.Base(MachineSurface.MutationsType));
        types.Add(NamespaceTypes.Base(MachineSurface.QueriesType));
        types.Add(
            NamespaceTypes.Field(
                "RootMutation",
                MachineSurface.RootField,
                MachineSurface.MutationsType
            )
        );
        types.Add(
            NamespaceTypes.Field("RootQuery", MachineSurface.RootField, MachineSurface.QueriesType)
        );
        types.Add(DiscardResultType());

        foreach (var machine in exposed)
            AddMachine(types, machine);
        types.AddRange(MachineChangedSubscription.Types(exposed));

        return new ValueTask<IReadOnlyCollection<ITypeSystemMember>>(types);
    }

    private static void AddMachine(List<ITypeSystemMember> types, IMachine machine)
    {
        var pascal = MachineSurface.PascalName(machine.Name);
        var field = MachineSurface.FieldName(machine.Name);
        var states = machine.States;

        types.Add(StateType(machine, pascal, states));
        var context = MachineContextFields.Of(machine, states);
        if (context.Count > 0)
            types.Add(ContextType(machine, pascal, context));
        types.Add(ResultType(machine, pascal, context.Count > 0));

        var mutations = MachineSurface.MutationTypeName(pascal);
        types.Add(NamespaceTypes.Base(mutations));
        types.Add(NamespaceTypes.Field(MachineSurface.MutationsType, field, mutations));
        types.Add(
            new ObjectTypeExtension(d =>
            {
                d.Name(mutations);
                AddStart(d, machine, pascal);
                AddDiscard(d, machine);
                foreach (var trigger in MachineSurface.ClientTriggers(machine))
                    AddEvent(d, machine, pascal, trigger);
            })
        );

        var queries = MachineSurface.QueryTypeName(pascal);
        types.Add(NamespaceTypes.Base(queries));
        types.Add(NamespaceTypes.Field(MachineSurface.QueriesType, field, queries));
        types.Add(
            new ObjectTypeExtension(d =>
            {
                d.Name(queries);
                Authorized(d.Field("get"))
                    .Description(
                        $"The caller's instance of '{machine.Name}' with this id, or null when the caller has none: "
                            + "another user's id is answered as an unknown one."
                    )
                    .Argument("id", a => a.Type<NonNullType<UuidType>>())
                    .Type(new NamedTypeNode(MachineSurface.ResultTypeName(pascal)))
                    .Resolve(ctx => MachineResolvers.GetAsync(ctx, machine));
            })
        );
    }

    private static void AddStart(IObjectTypeDescriptor d, IMachine machine, string pascal)
    {
        if (machine.StartInputType is not { } startType)
            return;

        var start = MachineWriteField.Mark(Authorized(d.Field("start")));
        start.Description(
            $"Starts an instance of '{machine.Name}' for the caller from its declared start input. The server mints "
                + "the id; a retry with the same requestId and input answers with the same instance."
        );
        var takesInput = MachineSurface.HasFields(startType);
        if (takesInput)
            start.Argument("input", a => a.Type(NonNullInput(startType)));
        start
            .Argument("requestId", a => a.Type<NonNullType<StringType>>())
            .Type(NonNullResult(pascal))
            .Resolve(ctx =>
                MachineResolvers.StartAsync(ctx, machine, takesInput ? startType : null)
            );
    }

    private static void AddDiscard(IObjectTypeDescriptor d, IMachine machine) =>
        MachineWriteField
            .Mark(Authorized(d.Field("discard")))
            .Description(
                $"Deletes the caller's instance of '{machine.Name}', cancelling a run its state invoked. A missing id, "
                    + "or another user's, answers as discarded."
            )
            .Argument("id", a => a.Type<NonNullType<UuidType>>())
            .Argument("requestId", a => a.Type<NonNullType<StringType>>())
            .Type(new NonNullTypeNode(new NamedTypeNode(MachineSurface.DiscardResultType)))
            .Resolve(ctx => MachineResolvers.DiscardAsync(ctx, machine));

    private static void AddEvent(
        IObjectTypeDescriptor d,
        IMachine machine,
        string pascal,
        MachineTrigger trigger
    )
    {
        var field = MachineWriteField.Mark(
            Authorized(d.Field(MachineSurface.EventField(trigger.Name)))
        );
        field.Argument("id", a => a.Type<NonNullType<UuidType>>());

        if (trigger.Kind == TriggerKind.EffectBound)
        {
            // The effect produces what the edge records, so the client sends no input, and cannot predict the result.
            field.Description(
                $"Sends {trigger.Name} to the caller's instance of '{machine.Name}': the server runs its irreversible "
                    + "effect once per requestId and records the result."
            );
            field.Argument("requestId", a => a.Type<NonNullType<StringType>>());
        }
        else
        {
            field.Description(
                $"Sends {trigger.Name} to the caller's instance of '{machine.Name}'. The server applies it, checking the "
                    + "event's rule and its input, and answers with what it stored."
            );
            if (trigger.InputType is { } input && MachineSurface.HasFields(input))
                field.Argument("input", a => a.Type(NonNullInput(input)));
            field.Argument("requestId", a => a.Type<NonNullType<StringType>>());
            field.Argument(
                "clientResult",
                a =>
                    a.Type<StringType>()
                        .Description(
                            "The canonical snapshot the client's twin predicts. A different result is refused as "
                                + "client-divergence and nothing is written."
                        )
            );
        }

        field
            .Type(NonNullResult(pascal))
            .Resolve(ctx => MachineResolvers.EventAsync(ctx, machine, trigger));
    }

    private static IObjectFieldDescriptor Authorized(IObjectFieldDescriptor field) =>
        field.Authorize(ApplyPolicy.BeforeResolver);

    private static EnumType StateType(
        IMachine machine,
        string pascal,
        IReadOnlyList<MachineState> states
    ) =>
        new(d =>
        {
            d.Name(MachineSurface.StateTypeName(pascal));
            d.Description($"The states of '{machine.Name}', named as the machine declares them.");
            foreach (var state in states)
                d.Value(state.Name).Name(state.Name);
        });

    private static ObjectType ContextType(
        IMachine machine,
        string pascal,
        IReadOnlyList<MachineContextField> fields
    ) =>
        new(d =>
        {
            d.Name(MachineSurface.ContextTypeName(pascal));
            d.Description(
                $"The context of an instance of '{machine.Name}': every field any of its states declares, null where "
                    + "the state the instance is in does not hold it."
            );
            foreach (var field in fields)
                d.Field(field.Name)
                    .Type(field.GraphQLType)
                    .Resolve(ctx => field.Read(ctx.Parent<JsonObject>()));
        });

    private static ObjectType ResultType(IMachine machine, string pascal, bool hasContext) =>
        new(d =>
        {
            d.Name(MachineSurface.ResultTypeName(pascal));
            d.Description(
                $"An instance of '{machine.Name}' as the server stored it, or the problem the operation was refused with."
            );
            d.Field("id")
                .Type<UuidType>()
                .Description("The instance's id.")
                .Resolve(ctx => ctx.Parent<MachineAnswer>().Id);
            d.Field("revision")
                .Type<LongType>()
                .Description(
                    "The instance's revision, bumped by every write. It travels beside the snapshot, never in it."
                )
                .Resolve(ctx => ctx.Parent<MachineAnswer>().Revision);
            d.Field("state")
                .Type(new NamedTypeNode(MachineSurface.StateTypeName(pascal)))
                .Resolve(ctx => ctx.Parent<MachineAnswer>().State);
            if (hasContext)
                d.Field("context")
                    .Type(new NamedTypeNode(MachineSurface.ContextTypeName(pascal)))
                    .Resolve(ctx => ctx.Parent<MachineAnswer>().Context);
            d.Field("dueAt")
                .Type<DateTimeType>()
                .Description(
                    "When the timer of the state the instance is in fires, or null when that state has no timer or "
                        + "the server cleared it. Says nothing when dueAtKnown is false."
                )
                .Resolve(ctx => ctx.Parent<MachineAnswer>().DueAt);
            d.Field("dueAtKnown")
                .Type<NonNullType<BooleanType>>()
                .Description(
                    "False when the answer replays a request the instance has moved past since: the server cannot "
                        + "know that state's due time, so dueAt is null and means nothing. Treat dueAt as absent then."
                )
                .Resolve(ctx => ctx.Parent<MachineAnswer>().DueAtKnown);
            d.Field("problem")
                .Type<ObjectType<SnapshotProblem>>()
                .Description("Why the operation was refused, or null when it was not.")
                .Resolve(ctx => ctx.Parent<MachineAnswer>().Problem);
        });

    private static ObjectType DiscardResultType() =>
        new(d =>
        {
            d.Name(MachineSurface.DiscardResultType);
            d.Description("What discarding a machine instance answers.");
            d.Field("id")
                .Type<NonNullType<UuidType>>()
                .Resolve(ctx => ctx.Parent<MachineDiscardAnswer>().Id);
            d.Field("discarded")
                .Type<NonNullType<BooleanType>>()
                .Resolve(ctx => ctx.Parent<MachineDiscardAnswer>().Discarded);
            d.Field("problem")
                .Type<ObjectType<SnapshotProblem>>()
                .Resolve(ctx => ctx.Parent<MachineDiscardAnswer>().Problem);
        });

    private static NonNullTypeNode NonNullResult(string pascal) =>
        new(new NamedTypeNode(MachineSurface.ResultTypeName(pascal)));

    private static Type NonNullInput(Type input) =>
        typeof(NonNullType<>).MakeGenericType(typeof(InputObjectType<>).MakeGenericType(input));
}

/// <summary>One field of a machine's context type: its JSON name, its GraphQL type and how it is read.</summary>
/// <param name="Name">The field's name, which is its JSON name in the stored context.</param>
/// <param name="ClrType">The type the declaring record gives it.</param>
internal sealed record MachineContextField(string Name, Type ClrType)
{
    /// <summary>The field's GraphQL type: the CLR type's, always nullable, since a state need not hold it.</summary>
    public Type GraphQLType =>
        ClrType.IsValueType && Nullable.GetUnderlyingType(ClrType) is null
            ? typeof(Nullable<>).MakeGenericType(ClrType)
            : ClrType;

    /// <summary>The field's value in <paramref name="context"/>, read as the record declares it, or null.</summary>
    public object? Read(JsonObject context) =>
        context.TryGetPropertyValue(Name, out var node) && node is not null
            ? node.Deserialize(GraphQLType, MachineSurface.Json)
            : null;
}

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
