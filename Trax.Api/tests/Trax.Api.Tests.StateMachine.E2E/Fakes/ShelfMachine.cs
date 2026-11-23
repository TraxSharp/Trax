using System.Text.Json.Nodes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Api.StateMachine.E2E.Fakes;

/// <summary>
/// A small declarative machine whose definition a test can change between two hosts, so the hosts serve two
/// versions of one machine, as two nodes do in the middle of a rolling deploy:
/// <code>
/// start --> Open --File--> Filed --Reopen--> Open
///                            │ Lapse (and, in V2, after 1 hour)
///                            ▼
///                          Lapsed --Reopen--> Open
/// </code>
/// </summary>
/// <remarks>
/// Every host built over this assembly discovers it and builds it as <see cref="ShelfShape.V1"/>; a test that needs
/// another shape sets <see cref="Shape"/> before it starts a host and puts it back after the host has built its
/// machines (<see cref="ShelfHosts"/> does both). The E2E fixtures run one at a time, so no other host is built in
/// between. Every rule is declared, so the machine never refuses a host that does not expose it.
/// </remarks>
public class ShelfMachine : Machine<ShelfState, ShelfTrigger>
{
    public const string MachineId = "shelf";

    /// <summary>How long <see cref="ShelfState.Filed"/> waits before it lapses, in <see cref="ShelfShape.V2WithTimer"/>.</summary>
    public static readonly TimeSpan LapseAfter = TimeSpan.FromHours(1);

    /// <summary>The shape the next <see cref="ShelfMachine"/> is built with.</summary>
    public static ShelfShape Shape { get; set; } = ShelfShape.V1;

    /// <summary>The client events, by the field name each has under <c>machines.shelf</c>.</summary>
    public static readonly IReadOnlyList<string> ClientEventFields = ["file", "reopen", "lapse"];

    protected override void Configure(IMachineBuilder<ShelfState, ShelfTrigger> m)
    {
        m.Id(MachineId)
            .Version(Shape == ShelfShape.V2 ? 2 : 1)
            .StartsAt(ShelfState.Open, () => new JsonObject());
        m.Get(r => r.OwnerOnly());
        m.Watch(r => r.OwnerOnly());
        m.Discard(r => r.OwnerOnly());
        // The second version reads the first's instances as they are, so a V2 node can move a row a V1 node wrote.
        if (Shape == ShelfShape.V2)
            m.MigrateFrom(1, (state, context) => new MigrationResult(state, context));

        if (Shape == ShelfShape.RecursiveContext)
        {
            ConfigureRecursiveContext(m);
            return;
        }

        m.Start<ShelfStartInput>(s =>
            s.Reduce(Set((ShelfContext c) => c.Label).FromInput((ShelfStartInput i) => i.Label))
                .AnyAuthenticated()
        );

        if (Shape == ShelfShape.RecursiveInput)
            m.In(ShelfState.Open)
                .Context<ShelfContext>()
                .On(ShelfTrigger.File)
                .WithInput<ShelfFileInput>()
                .AnyAuthenticated()
                .To(ShelfState.Filed);
        else
            m.In(ShelfState.Open)
                .Context<ShelfContext>()
                .On(ShelfTrigger.File)
                .AnyAuthenticated()
                .To(ShelfState.Filed);

        var filed = m.In(ShelfState.Filed)
            .Context<ShelfContext>()
            .On(ShelfTrigger.Reopen)
            .AnyAuthenticated()
            .To(ShelfState.Open)
            .On(ShelfTrigger.Lapse)
            .AnyAuthenticated()
            .To(ShelfState.Lapsed);
        if (Shape == ShelfShape.V2WithTimer)
            filed.After(LapseAfter).GoTo(ShelfState.Lapsed);

        m.In(ShelfState.Lapsed)
            .Context<ShelfContext>()
            .On(ShelfTrigger.Reopen)
            .AnyAuthenticated()
            .To(ShelfState.Open);
    }

    private static void ConfigureRecursiveContext(IMachineBuilder<ShelfState, ShelfTrigger> m)
    {
        m.Start<ShelfStartInput>(s =>
            s.Reduce(Set((ShelfTreeContext c) => c.Label).FromInput((ShelfStartInput i) => i.Label))
                .AnyAuthenticated()
        );
        m.In(ShelfState.Open)
            .Context<ShelfTreeContext>()
            .On(ShelfTrigger.File)
            .AnyAuthenticated()
            .To(ShelfState.Filed);
        m.In(ShelfState.Filed)
            .Context<ShelfTreeContext>()
            .On(ShelfTrigger.Reopen)
            .AnyAuthenticated()
            .To(ShelfState.Open)
            .On(ShelfTrigger.Lapse)
            .AnyAuthenticated()
            .To(ShelfState.Lapsed);
        m.In(ShelfState.Lapsed)
            .Context<ShelfTreeContext>()
            .On(ShelfTrigger.Reopen)
            .AnyAuthenticated()
            .To(ShelfState.Open);
    }
}
