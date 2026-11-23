using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Attributes;
using Trax.StateMachine.Persistence;

namespace Trax.Api.StateMachine.E2E.Fakes;

// The fakes as a host that serves them registers them: each a subclass marked [TraxMachine], since the attribute is
// the only thing that serves a machine. A host registers a fake's marked subclass in its place when it serves it, and
// the unmarked fake when it does not (E2EMachines). No host scans this assembly for machines, so neither is registered
// anywhere it is not named.

/// <summary><see cref="WizardMachine"/>, served.</summary>
[TraxMachine]
public sealed class ServedWizardMachine : WizardMachine;
