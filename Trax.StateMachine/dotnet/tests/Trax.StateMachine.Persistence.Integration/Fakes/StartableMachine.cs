using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Trax.StateMachine.Rules;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

/// <summary>
/// A note any authenticated caller starts from a <see cref="ListStart"/>: its title becomes the context's.
/// <c>Drafting -Finish-&gt; Finished</c>.
/// </summary>
public sealed class StartableMachine : Machine<ListState, ListTrigger>
{
    public const string Id = "startable";

    protected override void Configure(IMachineBuilder<ListState, ListTrigger> m) => Declare(m, Id);

    internal static void Declare(IMachineBuilder<ListState, ListTrigger> m, string id)
    {
        m.Id(id)
            .Version(1)
            .StartsAt(
                ListState.Drafting,
                () => new JsonObject { ["title"] = "untitled", ["copies"] = 1 }
            )
            .Start<ListStart>(s =>
                s.Reduce(Set((ListContext c) => c.Title).FromInput((ListStart i) => i.Title))
                    .AnyAuthenticated()
            )
            .Get(r => r.OwnerOnly())
            .Watch(r => r.OwnerOnly())
            .Discard(r => r.OwnerOnly());

        m.In(ListState.Drafting)
            .Context<ListContext>()
            .On(ListTrigger.Finish)
            .AnyAuthenticated()
            .To(ListState.Finished);

        m.In(ListState.Finished).Context<ListContext>();
    }
}
