---
title: Undo an effect that already ran
description: "How to start over before a state machine's effect runs, and how to compensate one that ran (a refund, a void) with a machine of its own."
type: how-to
area: machines
nav_order: 5
---

# Undo an effect that already ran

A machine's effect is irreversible, and the draft it ran on is the record that it ran. What a cancellation means
depends on where the draft is relative to its effect, and it is declared in the machine, because only the domain
knows what it undoes. The effect itself is described on [ISnapshotEffect](/docs/sdk-reference/statemachine-api/i-snapshot-effect).

## Before the effect runs

Nothing irreversible has happened yet.

- Discard the draft with the machine's `discard` (or start a new one and leave the old one to the
  [draft TTL](/docs/sdk-reference/statemachine-api/add-state-machines#options)).
- Or declare a reset edge from the state back to the initial state. The next send after a reset is a new intent with
  a new effect key.

## While the effect runs

The draft takes no other write until the send settles: an event or a reset is refused as `effect-in-progress`, a
discard as `discard-not-allowed`. Show the user that it is processing, and retry the send with the same request id
to read its result.

## After the effect ran

The draft stays in its committed state. Undoing the effect is a second irreversible action (a refund, a void,
releasing stock), not a delete, and a machine runs exactly one effect. So the compensation is a machine of its own,
whose one effect is the undo, started from the receipt the first machine recorded.

1. Declare the compensating machine. Its start takes the receipt to undo; its one event runs the undo effect.

   ```csharp compile=refund
   using System.Text.Json.Nodes;
   using Trax.Effect.StateMachine;
   using Trax.Effect.StateMachine.Persistence;
   using static Trax.Effect.StateMachine.Rules;

   public sealed record RefundStart
   {
       public string ChargeId { get; init; } = "";
   }

   public sealed record RequestedContext
   {
       public string ChargeId { get; init; } = "";
   }

   public sealed record RefundInput
   {
       public string Receipt { get; init; } = "";
   }

   public sealed record RefundedContext
   {
       public string ChargeId { get; init; } = "";

       public string RefundId { get; init; } = "";
   }

   public sealed class RefundMachine : Machine<RefundState, RefundTrigger>
   {
       protected override void Configure(IMachineBuilder<RefundState, RefundTrigger> m)
       {
           m.Id("refund")
               .Version(1)
               .StartsAt(RefundState.Requested, () => new JsonObject { ["chargeId"] = "" })
               .Start<RefundStart>(s =>
                   s.Reduce(
                           Set((RequestedContext c) => c.ChargeId)
                               .FromInput((RefundStart i) => i.ChargeId)
                       )
                       .Requires("Support")
               )
               .Get(r => r.OwnerOnly())
               .Watch(r => r.OwnerOnly())
               .Discard(r => r.OwnerOnly());

           m.In(RefundState.Requested)
               .Context<RequestedContext>()
               .On(RefundTrigger.Refund)
               .Requires("Support")
               .WithInput<RefundInput>()
               .When(Field((RequestedContext c) => c.ChargeId).NonEmpty())
               .Because("A refund names the charge it refunds.")
               .RunsOnce<IRefund>()
               .Reduce(Set((RefundedContext c) => c.RefundId).FromInput((RefundInput i) => i.Receipt))
               .To(RefundState.Refunded);

           m.In(RefundState.Refunded).Context<RefundedContext>().Committed();
       }
   }

   public interface IRefund : ISnapshotEffect;
   ```

2. Implement the undo. Pass the downstream service an idempotency key derived from the charge, not only the effect
   key: Trax runs the refund once per refund instance, and two refund instances for one charge are two instances.

   ```csharp compile=refund
   using Trax.Effect.StateMachine;

   public sealed class GatewayRefund(IPaymentGateway gateway) : IRefund
   {
       public Task<string> Run(Snapshot snapshot, CancellationToken cancellationToken = default)
       {
           var chargeId = snapshot.Context["chargeId"]!.GetValue<string>();
           return gateway.Refund(chargeId, idempotencyKey: $"refund:{chargeId}", cancellationToken);
       }
   }
   ```

3. Register both, and mark the machine `[TraxMachine]` if clients start it through its typed operations.

   ```csharp compile=refund
   services.AddScoped<IRefund, GatewayRefund>();
   ```

4. Start a refund with the checkout's receipt as `chargeId`, then send `Refund`.

The checkout instance keeps showing the charge and the refund instance shows the refund; together they are the
history a support request needs.

## See also

- [ISnapshotEffect](/docs/sdk-reference/statemachine-api/i-snapshot-effect), how an effect runs once and records its receipt.
- [Machine](/docs/sdk-reference/statemachine-api/machine), the builder methods used above.
- [Authoring a machine](/docs/statemachine/authoring).
