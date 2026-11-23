using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Api.Tests.Fakes.Machines;

// The machines of the typed machine surface's schema tests. Each is marked [TraxMachine], so it is served wherever it
// is registered; no host scans this assembly, and a test registers only the machines it names (MachineSchemaHost), so
// a machine that refuses startup once served (a raw-delegate one, a name that collides) refuses only the host of the
// test about it. Every machine here builds.

public enum ShopState
{
    Browsing,
    Ordered,
    Approved,
    Expired,
}
