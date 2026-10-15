import { afterEach, describe, expect, it, vi } from 'vitest';
import { reminder } from '../machines/reminder/reminder.machine.g';
import type { ReminderSpec } from '../machines/reminder/reminder.contexts.g';
import { turnstile, type TurnstileSpec } from '../machines/turnstile/turnstile';
import type { IrDocument } from '../rules/irMachine';
import { typedMachineFromIr } from '../typed';
import { checkout, type CheckoutSpec } from '../machines/checkout/checkout';
import { ServerMachineController, type ServerMachineOptions } from './serverController';
import {
  FakeMachineServer,
  flushMicrotasks,
  sequentialRequestIds,
  serverSnapshot,
} from './testing/FakeMachineServer';

// The server-authoritative controller: the server owns the instance, the twin predicts, every event carries
// its own requestId, and pushed snapshots are ordered by revision within one incarnation. Red until the
// controller exists (its stub throws); drop `.fails` from each test as it passes.

afterEach(() => {
  vi.useRealTimers();
});

const unlocked = (paidWith: string) => ({ state: 'Unlocked', context: { paidWith } });

/** A controller over instance `i-1`, loaded at Locked, revision 1, incarnation row-1. */
async function ready(options: Partial<ServerMachineOptions<TurnstileSpec>> = {}) {
  const server = new FakeMachineServer();
  const controller = new ServerMachineController<TurnstileSpec>({
    machine: turnstile,
    server,
    id: 'i-1',
    newRequestId: sequentialRequestIds(),
    ...options,
  });
  const started = controller.start();
  await flushMicrotasks();
  server.gets[0].resolve({ kind: 'ok', snapshot: serverSnapshot() });
  await started;
  await flushMicrotasks();
  return { server, controller };
}

describe('ServerMachineController', () => {
  it('rolls back on a transport error and keeps the last server snapshot', async () => {
    const { server, controller } = await ready();

    const sent = controller.send('Coin', { coin: 'quarter' });
    await flushMicrotasks();
    expect(controller.get().snapshot?.state).toBe('Unlocked');
    server.events[0].fail();

    expect(await sent).toMatchObject({ outcome: 'failed' });
    const view = controller.get();
    expect(view.snapshot).toEqual(turnstile.snapshotAt('Locked', {}));
    expect(view.confirmed).toEqual(turnstile.snapshotAt('Locked', {}));
    expect(view.revision).toBe(1);
    expect(view.problem).not.toBeNull();
    expect(view.pending).toBe(false);
  });

  it('sends an event the twin refuses, without a prediction', async () => {
    const { server, controller } = await ready();

    void controller.send('Push');
    await flushMicrotasks();

    expect(server.events).toHaveLength(1);
    expect(server.events[0].request).toMatchObject({ id: 'i-1', trigger: 'Push' });
    expect(server.events[0].request.clientResult).toBeUndefined();
    expect(controller.get().snapshot?.state).toBe('Locked');
    expect(controller.get().pending).toBe(true);
  });

  it('does not predict through an effect-bound event even when the twin could', async () => {
    const server = new FakeMachineServer();
    const controller = new ServerMachineController<CheckoutSpec>({
      machine: checkout,
      server,
      id: 'c-1',
      newRequestId: sequentialRequestIds(),
    });
    const started = controller.start();
    await flushMicrotasks();
    const review = { currency: 'USD', items: ['latte'], receipt: null, total: 4 };
    server.gets[0].resolve({ kind: 'ok', snapshot: serverSnapshot({ state: 'Review', context: review }) });
    await started;
    await flushMicrotasks();

    expect(checkout.isEffectBound('Pay')).toBe(true);
    expect(controller.can('Pay')).toBe(true);
    // A receipt the types do not allow, so a prediction would succeed if the controller tried one.
    const receipt = { receipt: 'r-1' };
    expect(checkout.core.advance(checkout.snapshotAt('Review', review), 'Pay', receipt)).toMatchObject({
      outcome: 'transitioned',
    });
    void controller.send('Pay', ...([receipt] as unknown as []));
    void controller.send('Restart');
    await flushMicrotasks();

    expect(server.events[0].request).toMatchObject({ id: 'c-1', trigger: 'Pay' });
    expect(server.events[0].request.clientResult).toBeUndefined();
    expect(controller.get().snapshot?.state).toBe('Review');
  });

  it('can for an effect-bound trigger still reads its guard\'s context rules', async () => {
    // No invariant on Open, so only the guard's own context rule keeps Pay from an empty order.
    interface OrderSpec {
      states: { Open: { items: string[]; receipt: string | null }; Paid: { items: string[]; receipt: string | null } };
      triggers: { Pay: undefined };
    }
    const fields = [
      { name: 'items', type: 'array', nullable: false, values: { type: 'string', nullable: false }, constraints: [] },
      { name: 'receipt', type: 'string', nullable: true, constraints: [] },
    ];
    const order = typedMachineFromIr<OrderSpec>({
      irFormat: 1,
      id: 'order',
      version: 1,
      initialState: 'Open',
      initialContext: { items: [], receipt: null },
      states: ['Open', 'Paid'],
      triggers: ['Pay'],
      committedStates: ['Paid'],
      context: { Open: { fields }, Paid: { fields } },
      inputs: {},
      transitions: [
        {
          from: 'Open',
          trigger: 'Pay',
          to: 'Paid',
          effect: { keyPrefix: 'order:charge' },
          guard: {
            rule: 'all',
            rules: [
              { rule: 'count', source: 'context', field: 'items', op: 'gt', value: 0 },
              { rule: 'present', source: 'input', field: 'receipt' },
            ],
          },
          reduce: { reduce: 'set', steps: [{ field: 'receipt', value: { input: 'receipt' } }] },
        },
      ],
    } as IrDocument);
    expect(order.isEffectBound('Pay')).toBe(true);

    const opened = async (items: string[]) => {
      const server = new FakeMachineServer();
      const controller = new ServerMachineController<OrderSpec>({
        machine: order,
        server,
        id: 'o-1',
        newRequestId: sequentialRequestIds(),
      });
      const started = controller.start();
      await flushMicrotasks();
      server.gets[0].resolve({ kind: 'ok', snapshot: serverSnapshot({ state: 'Open', context: { items, receipt: null } }) });
      await started;
      await flushMicrotasks();
      return controller;
    };

    expect((await opened([])).can('Pay')).toBe(false);
    expect((await opened(['latte'])).can('Pay')).toBe(true);
  });

  it('sends the prediction as clientResult', async () => {
    const { server, controller } = await ready();

    void controller.send('Coin', { coin: 'quarter' });
    await flushMicrotasks();

    const predicted = turnstile.snapshotAt('Unlocked', { paidWith: 'quarter' });
    expect(server.events[0].request).toMatchObject({
      trigger: 'Coin',
      input: { coin: 'quarter' },
      clientResult: turnstile.serialize(predicted),
    });
    expect(controller.get().snapshot).toEqual(predicted);
  });

  it('a client-divergence refusal reloads the server snapshot once', async () => {
    const { server, controller } = await ready();

    void controller.send('Coin', { coin: 'quarter' });
    await flushMicrotasks();
    server.events[0].resolve({ kind: 'problem', code: 'CLIENT_DIVERGENCE', message: 'diverged' });
    await flushMicrotasks();

    expect(server.gets).toHaveLength(2);
    server.gets[1].resolve({ kind: 'ok', snapshot: serverSnapshot({ revision: 2, ...unlocked('dollar') }) });
    await flushMicrotasks();

    expect(server.gets).toHaveLength(2);
    expect(controller.get().snapshot).toEqual(turnstile.snapshotAt('Unlocked', { paidWith: 'dollar' }));
    expect(controller.get().revision).toBe(2);
  });

  it('serialises events per instance, each with its own requestId', async () => {
    const { server, controller } = await ready();

    void controller.send('Coin', { coin: 'quarter' });
    void controller.send('Push');
    await flushMicrotasks();
    expect(server.events).toHaveLength(1);

    server.events[0].resolve({ kind: 'ok', snapshot: serverSnapshot({ revision: 2, ...unlocked('quarter') }) });
    await flushMicrotasks();

    expect(server.events).toHaveLength(2);
    expect(server.events.map((e) => e.request.trigger)).toEqual(['Coin', 'Push']);
    const [first, second] = server.events.map((e) => e.request.requestId);
    expect(first).toMatch(/^rq-\d+$/);
    expect(second).toMatch(/^rq-\d+$/);
    expect(first).not.toBe(second);
  });

  it('a refused first event drops the dependent prediction of the second', async () => {
    const { server, controller } = await ready();

    void controller.send('Coin', { coin: 'quarter' });
    void controller.send('Push'); // predicted from the Coin prediction: Unlocked, then Push to Locked
    await flushMicrotasks();
    server.events[0].resolve({ kind: 'problem', code: 'GUARD_FAILED', message: 'no' });
    await flushMicrotasks();

    // From the server's Locked, the twin cannot push, so the second event goes without a prediction.
    expect(server.events).toHaveLength(2);
    expect(server.events[1].request.trigger).toBe('Push');
    expect(server.events[1].request.clientResult).toBeUndefined();
    expect(controller.get().snapshot).toEqual(controller.get().confirmed);
    expect(controller.get().problem?.code).toBe('GUARD_FAILED');
  });

  it('retries a failed request with the same requestId; a new send gets a new one', async () => {
    const { server, controller } = await ready();

    void controller.send('Coin', { coin: 'quarter' });
    await flushMicrotasks();
    server.events[0].fail();
    await flushMicrotasks();

    void controller.retry();
    await flushMicrotasks();
    expect(server.events).toHaveLength(2);
    expect(server.events[1].request).toEqual(server.events[0].request);
    server.events[1].resolve({ kind: 'ok', snapshot: serverSnapshot({ revision: 2, ...unlocked('quarter') }) });
    await flushMicrotasks();

    void controller.send('Push');
    await flushMicrotasks();
    expect(server.events[2].request.requestId).not.toBe(server.events[0].request.requestId);
  });

  it('a later confirmed event supersedes a failed one: retry resends nothing', async () => {
    const { server, controller } = await ready();

    void controller.send('Coin', { coin: 'quarter' });
    await flushMicrotasks();
    server.events[0].fail();
    await flushMicrotasks();

    // The page moves on: a later event is confirmed, from the state the server holds.
    void controller.send('Push');
    await flushMicrotasks();
    server.events[1].resolve({ kind: 'ok', snapshot: serverSnapshot({ revision: 2 }) });
    await flushMicrotasks();

    // Resending the failed Coin now would apply it after Push, out of the order the page sent them.
    const retried = controller.retry();
    await flushMicrotasks();
    expect(server.events).toHaveLength(2);
    expect(await retried).toMatchObject({ outcome: 'failed', problem: { code: 'NOTHING_TO_RETRY' } });
    expect(controller.get().revision).toBe(2);
  });

  it('a later event refused or failed leaves the newest failure to retry', async () => {
    const { server, controller } = await ready();

    void controller.send('Coin', { coin: 'quarter' });
    await flushMicrotasks();
    server.events[0].fail();
    await flushMicrotasks();

    // A refusal moves nothing on the server, so the failed event is still the one to resend.
    void controller.send('Push');
    await flushMicrotasks();
    server.events[1].resolve({ kind: 'problem', code: 'GUARD_FAILED', message: 'no' });
    await flushMicrotasks();

    void controller.retry();
    await flushMicrotasks();
    expect(server.events).toHaveLength(3);
    expect(server.events[2].request).toEqual(server.events[0].request);
  });

  it('a start that failed in transport is resent by retry under its requestId, creating one instance', async () => {
    const server = new FakeMachineServer();
    const controller = new ServerMachineController<TurnstileSpec>({
      machine: turnstile,
      server,
      startInput: {},
      newRequestId: sequentialRequestIds(),
    });

    void controller.start();
    await flushMicrotasks();
    server.starts[0].fail();
    await flushMicrotasks();
    expect(controller.get().problem?.code).toBe('TRANSPORT_ERROR');
    expect(controller.get().status).toBe('loading');

    const retried = controller.retry();
    await flushMicrotasks();
    expect(server.starts).toHaveLength(2);
    expect(server.starts[1].request).toEqual(server.starts[0].request);
    server.starts[1].resolve({ kind: 'started', id: 'i-1', snapshot: serverSnapshot() });

    expect(await retried).toMatchObject({ outcome: 'confirmed' });
    const view = controller.get();
    expect(view.status).toBe('ready');
    expect(view.id).toBe('i-1');
    expect(view.problem).toBeNull();
    expect(server.openSubscriptions('i-1')).toHaveLength(1);

    // The start is spent: a retry now has nothing to resend, and nothing is started again.
    expect(await controller.retry()).toMatchObject({ outcome: 'failed', problem: { code: 'NOTHING_TO_RETRY' } });
    expect(server.starts).toHaveLength(2);
  });

  it('a start the server refused is not resent by retry', async () => {
    const server = new FakeMachineServer();
    const controller = new ServerMachineController<TurnstileSpec>({ machine: turnstile, server, startInput: {} });

    void controller.start();
    await flushMicrotasks();
    server.starts[0].resolve({ kind: 'problem', code: 'TOO_MANY_DRAFTS', message: 'cap' });
    await flushMicrotasks();

    expect(await controller.retry()).toMatchObject({ outcome: 'failed', problem: { code: 'NOTHING_TO_RETRY' } });
    expect(server.starts).toHaveLength(1);
  });

  it('server-outdated is retried with backoff and never reloads', async () => {
    const { server, controller } = await ready({ retryDelays: [100, 200] });
    vi.useFakeTimers();

    const sent = controller.send('Coin', { coin: 'quarter' });
    await flushMicrotasks();
    server.events[0].resolve({ kind: 'problem', code: 'SERVER_OUTDATED', message: 'newer node' });
    await flushMicrotasks();
    expect(server.events).toHaveLength(1);

    vi.advanceTimersByTime(100);
    await flushMicrotasks();
    expect(server.events).toHaveLength(2);
    server.events[1].resolve({ kind: 'problem', code: 'SERVER_OUTDATED', message: 'newer node' });
    await flushMicrotasks();

    vi.advanceTimersByTime(199);
    await flushMicrotasks();
    expect(server.events).toHaveLength(2);
    vi.advanceTimersByTime(1);
    await flushMicrotasks();
    expect(server.events).toHaveLength(3);
    server.events[2].resolve({ kind: 'ok', snapshot: serverSnapshot({ revision: 2, ...unlocked('quarter') }) });

    expect(await sent).toMatchObject({ outcome: 'confirmed' });
    expect(new Set(server.events.map((e) => e.request.requestId)).size).toBe(1);
    expect(server.gets).toHaveLength(1);
    expect(controller.get().status).toBe('ready');
  });

  it('a start answered server-outdated is retried with backoff under the same requestId', async () => {
    vi.useFakeTimers();
    const server = new FakeMachineServer();
    const controller = new ServerMachineController<TurnstileSpec>({
      machine: turnstile,
      server,
      startInput: {},
      newRequestId: sequentialRequestIds(),
      retryDelays: [100, 200],
    });

    void controller.start();
    await flushMicrotasks();
    expect(server.starts).toHaveLength(1);
    server.starts[0].resolve({ kind: 'problem', code: 'SERVER_OUTDATED', message: 'newer node' });
    await flushMicrotasks();
    expect(controller.get().status).not.toBe('stopped');

    vi.advanceTimersByTime(99);
    await flushMicrotasks();
    expect(server.starts).toHaveLength(1);
    vi.advanceTimersByTime(1);
    await flushMicrotasks();
    expect(server.starts).toHaveLength(2);
    server.starts[1].resolve({ kind: 'problem', code: 'SERVER_OUTDATED', message: 'newer node' });
    await flushMicrotasks();

    vi.advanceTimersByTime(200);
    await flushMicrotasks();
    expect(server.starts).toHaveLength(3);
    server.starts[2].resolve({ kind: 'started', id: 'i-1', snapshot: serverSnapshot() });
    await flushMicrotasks();

    expect(new Set(server.starts.map((s) => s.request.requestId)).size).toBe(1);
    expect(controller.get().status).toBe('ready');
    expect(server.log).toContain('subscribe');
  });

  it('a start still answered server-outdated after the last retry shows the problem', async () => {
    vi.useFakeTimers();
    const server = new FakeMachineServer();
    const controller = new ServerMachineController<TurnstileSpec>({
      machine: turnstile,
      server,
      startInput: {},
      retryDelays: [100],
    });

    void controller.start();
    await flushMicrotasks();
    server.starts[0].resolve({ kind: 'problem', code: 'SERVER_OUTDATED', message: 'newer node' });
    await flushMicrotasks();
    vi.advanceTimersByTime(100);
    await flushMicrotasks();
    server.starts[1].resolve({ kind: 'problem', code: 'SERVER_OUTDATED', message: 'newer node' });
    await flushMicrotasks();

    expect(server.starts).toHaveLength(2);
    expect(controller.get().problem?.code).toBe('SERVER_OUTDATED');
    expect(controller.get().status).not.toBe('stopped');
  });

  it('the first event is the current snapshot', async () => {
    const server = new FakeMachineServer();
    const controller = new ServerMachineController<TurnstileSpec>({ machine: turnstile, server, id: 'i-1' });

    void controller.start();
    await flushMicrotasks();

    // The subscription is registered before the initial read, so no write can fall between the two.
    expect(server.log.indexOf('subscribe')).toBeGreaterThanOrEqual(0);
    expect(server.log.indexOf('subscribe')).toBeLessThan(server.log.indexOf('get'));

    server.stream('i-1').push({ type: 'snapshot', snapshot: serverSnapshot({ revision: 3, ...unlocked('quarter') }) });
    await flushMicrotasks();
    expect(controller.get().status).toBe('ready');
    expect(controller.get().snapshot).toEqual(turnstile.snapshotAt('Unlocked', { paidWith: 'quarter' }));
    expect(controller.get().revision).toBe(3);
  });

  it('drops any revision at or below the one it holds, its own results included', async () => {
    const { server, controller } = await ready();
    const stream = server.stream('i-1');

    stream.push({ type: 'snapshot', snapshot: serverSnapshot({ revision: 3, ...unlocked('quarter') }) });
    stream.push({ type: 'snapshot', snapshot: serverSnapshot({ revision: 2 }) });
    stream.push({ type: 'snapshot', snapshot: serverSnapshot({ revision: 3 }) });
    await flushMicrotasks();
    expect(controller.get().snapshot).toEqual(turnstile.snapshotAt('Unlocked', { paidWith: 'quarter' }));

    void controller.send('Push');
    await flushMicrotasks();
    server.events[0].resolve({ kind: 'ok', snapshot: serverSnapshot({ revision: 2 }) });
    await flushMicrotasks();

    expect(controller.get().revision).toBe(3);
    expect(controller.get().confirmed).toEqual(turnstile.snapshotAt('Unlocked', { paidWith: 'quarter' }));
    expect(controller.get().snapshot).toEqual(controller.get().confirmed);
  });

  it('a skipped revision just renders the newer snapshot', async () => {
    const { server, controller } = await ready();
    const stream = server.stream('i-1');

    stream.push({ type: 'snapshot', snapshot: serverSnapshot({ revision: 3, ...unlocked('quarter') }) });
    stream.push({ type: 'snapshot', snapshot: serverSnapshot({ revision: 5, ...unlocked('dollar') }) });
    await flushMicrotasks();

    expect(controller.get().revision).toBe(5);
    expect(controller.get().snapshot).toEqual(turnstile.snapshotAt('Unlocked', { paidWith: 'dollar' }));
    expect(server.gets).toHaveLength(1);
  });

  it('own result rev N then push rev N renders once', async () => {
    const { server, controller } = await ready();
    const listener = vi.fn();
    controller.subscribe(listener);

    void controller.send('Coin', { coin: 'quarter' });
    await flushMicrotasks();
    server.events[0].resolve({ kind: 'ok', snapshot: serverSnapshot({ revision: 2, ...unlocked('quarter') }) });
    await flushMicrotasks();
    const notified = listener.mock.calls.length;
    expect(controller.get().revision).toBe(2);

    server.stream('i-1').push({ type: 'snapshot', snapshot: serverSnapshot({ revision: 2, ...unlocked('quarter') }) });
    await flushMicrotasks();

    expect(listener).toHaveBeenCalledTimes(notified);
    expect(controller.get().revision).toBe(2);
  });

  it('an incarnation change reloads once', async () => {
    const { server, controller } = await ready();
    const stream = server.stream('i-1');
    stream.push({ type: 'snapshot', snapshot: serverSnapshot({ revision: 5 }) });
    await flushMicrotasks();

    // The row was deleted and re-created: a new rowId, whose revisions restart.
    stream.push({ type: 'snapshot', snapshot: serverSnapshot({ rowId: 'row-2', revision: 1 }) });
    stream.push({ type: 'snapshot', snapshot: serverSnapshot({ rowId: 'row-2', revision: 2 }) });
    await flushMicrotasks();
    expect(server.gets).toHaveLength(2);

    server.gets[1].resolve({ kind: 'ok', snapshot: serverSnapshot({ rowId: 'row-2', revision: 2, ...unlocked('dollar') }) });
    await flushMicrotasks();
    expect(server.gets).toHaveLength(2);
    expect(controller.get().rowId).toBe('row-2');
    expect(controller.get().revision).toBe(2);
    expect(controller.get().snapshot).toEqual(turnstile.snapshotAt('Unlocked', { paidWith: 'dollar' }));
  });

  it('a listener reconnect reloads once', async () => {
    const { server, controller } = await ready();

    server.stream('i-1').push({ type: 'reconnected' });
    await flushMicrotasks();
    expect(server.gets).toHaveLength(2);

    server.gets[1].resolve({ kind: 'ok', snapshot: serverSnapshot({ revision: 4, ...unlocked('dollar') }) });
    await flushMicrotasks();
    expect(server.gets).toHaveLength(2);
    expect(controller.get().revision).toBe(4);
  });

  it('a server-made transition while a prediction is pending wins', async () => {
    const { server, controller } = await ready();

    void controller.send('Coin', { coin: 'quarter' });
    await flushMicrotasks();
    expect(controller.get().snapshot).toEqual(turnstile.snapshotAt('Unlocked', { paidWith: 'quarter' }));

    // The server moved the instance itself (a timer, say), then refused the client's event.
    server.stream('i-1').push({ type: 'snapshot', snapshot: serverSnapshot({ revision: 2, ...unlocked('dollar') }) });
    await flushMicrotasks();
    server.events[0].resolve({ kind: 'problem', code: 'NO_TRANSITION', message: 'already unlocked' });
    await flushMicrotasks();

    expect(controller.get().snapshot).toEqual(turnstile.snapshotAt('Unlocked', { paidWith: 'dollar' }));
    expect(controller.get().revision).toBe(2);
    expect(controller.get().problem?.code).toBe('NO_TRANSITION');
  });

  describe('dueAt', () => {
    const due = '2026-10-12T10:05:00Z';

    it('takes the due time from every accepted answer and push, null included', async () => {
      const { server, controller } = await ready();
      expect(controller.get().dueAt).toBeNull();

      void controller.send('Coin', { coin: 'quarter' });
      await flushMicrotasks();
      server.events[0].resolve({ kind: 'ok', snapshot: serverSnapshot({ revision: 2, ...unlocked('q'), dueAt: due }) });
      await flushMicrotasks();
      expect(controller.get().dueAt).toBe(due);

      // A push of a write that left the timed state carries null: the timer is gone.
      server.stream('i-1').push({ type: 'snapshot', snapshot: serverSnapshot({ revision: 3, dueAt: null }) });
      await flushMicrotasks();
      expect(controller.get().dueAt).toBeNull();

      // A push that enters it again carries the new due time.
      const later = '2026-10-12T10:20:00Z';
      server.stream('i-1').push({ type: 'snapshot', snapshot: serverSnapshot({ revision: 4, ...unlocked('d'), dueAt: later }) });
      await flushMicrotasks();
      expect(controller.get().dueAt).toBe(later);
    });

    it('keeps the due time a dropped stale answer would replace', async () => {
      const { server, controller } = await ready();
      server.stream('i-1').push({ type: 'snapshot', snapshot: serverSnapshot({ revision: 3, ...unlocked('q'), dueAt: due }) });
      await flushMicrotasks();

      void controller.send('Push');
      await flushMicrotasks();
      server.events[0].resolve({ kind: 'ok', snapshot: serverSnapshot({ revision: 2, dueAt: null }) });
      await flushMicrotasks();

      expect(controller.get().revision).toBe(3);
      expect(controller.get().dueAt).toBe(due);
    });

    it('an answer without the field keeps the due time only while the state stays', async () => {
      const { server, controller } = await ready();
      const stream = server.stream('i-1');
      stream.push({ type: 'snapshot', snapshot: serverSnapshot({ revision: 2, ...unlocked('q'), dueAt: due }) });
      await flushMicrotasks();

      // The same state, written again: a write that stays in a timed state keeps its due time.
      stream.push({ type: 'snapshot', snapshot: serverSnapshot({ revision: 3, ...unlocked('d') }) });
      await flushMicrotasks();
      expect(controller.get().dueAt).toBe(due);

      // Another state: the held due time was the old state's, so it is not shown for this one.
      stream.push({ type: 'snapshot', snapshot: serverSnapshot({ revision: 4 }) });
      await flushMicrotasks();
      expect(controller.get().dueAt).toBeNull();
    });

    describe('in a state the machine times', () => {
      async function readyReminder() {
        const server = new FakeMachineServer();
        const controller = new ServerMachineController<ReminderSpec>({
          machine: reminder,
          server,
          id: 'r-1',
          newRequestId: sequentialRequestIds(),
        });
        const started = controller.start();
        await flushMicrotasks();
        server.gets[0].resolve({ kind: 'ok', snapshot: serverSnapshot({ state: 'Idle' }) });
        await started;
        await flushMicrotasks();
        return { server, controller };
      }

      it('an answer that enters it without a due time reads the instance for one', async () => {
        const { server, controller } = await readyReminder();

        // A replayed answer whose revision the row has moved past does not know the due time, and leaves it out.
        void controller.send('Open');
        await flushMicrotasks();
        server.events[0].resolve({ kind: 'ok', snapshot: serverSnapshot({ revision: 2, state: 'Review' }) });
        await flushMicrotasks();

        expect(server.gets).toHaveLength(2);
        server.gets[1].resolve({ kind: 'ok', snapshot: serverSnapshot({ revision: 3, state: 'Review', dueAt: due }) });
        await flushMicrotasks();
        expect(controller.get().snapshot?.state).toBe('Review');
        expect(controller.get().dueAt).toBe(due);
      });

      it('a read of the same revision fills in the due time', async () => {
        const { server, controller } = await readyReminder();

        void controller.send('Open');
        await flushMicrotasks();
        server.events[0].resolve({ kind: 'ok', snapshot: serverSnapshot({ revision: 2, state: 'Review' }) });
        await flushMicrotasks();
        server.gets[1].resolve({ kind: 'ok', snapshot: serverSnapshot({ revision: 2, state: 'Review', dueAt: due }) });
        await flushMicrotasks();

        expect(controller.get().revision).toBe(2);
        expect(controller.get().dueAt).toBe(due);
      });

      it('keeps the due time it holds when the state stays and the answer does not know it, and reads nothing', async () => {
        const { server, controller } = await readyReminder();
        server.stream('r-1').push({ type: 'snapshot', snapshot: serverSnapshot({ revision: 2, state: 'Review', dueAt: due }) });
        await flushMicrotasks();

        void controller.send('Extend');
        await flushMicrotasks();
        server.events[0].resolve({ kind: 'ok', snapshot: serverSnapshot({ revision: 3, state: 'Review' }) });
        await flushMicrotasks();

        expect(controller.get().revision).toBe(3);
        expect(controller.get().dueAt).toBe(due);
        expect(server.gets).toHaveLength(1);
      });

      it('a read that still answers no due time is not read again', async () => {
        const { server, controller } = await readyReminder();

        void controller.send('Open');
        await flushMicrotasks();
        server.events[0].resolve({ kind: 'ok', snapshot: serverSnapshot({ revision: 2, state: 'Review' }) });
        await flushMicrotasks();
        server.gets[1].resolve({ kind: 'ok', snapshot: serverSnapshot({ revision: 3, state: 'Review', dueAt: null }) });
        await flushMicrotasks();

        expect(server.gets).toHaveLength(2);
        expect(controller.get().revision).toBe(3);
        expect(controller.get().dueAt).toBeNull();
      });

      it('a push in the same state with a null due time clears the deadline the server gave up on', async () => {
        const { server, controller } = await readyReminder();
        const stream = server.stream('r-1');
        stream.push({ type: 'snapshot', snapshot: serverSnapshot({ revision: 2, state: 'Review', dueAt: due }) });
        await flushMicrotasks();
        expect(controller.get().dueAt).toBe(due);

        // The server stopped trying to fire the timer and cleared it, staying in Review: null means cleared.
        stream.push({ type: 'snapshot', snapshot: serverSnapshot({ revision: 3, state: 'Review', dueAt: null }) });
        await flushMicrotasks();

        expect(controller.get().revision).toBe(3);
        expect(controller.get().dueAt).toBeNull();
        expect(server.gets).toHaveLength(1);
      });

      it('an answer in the same state with a null due time clears it too', async () => {
        const { server, controller } = await readyReminder();
        server.stream('r-1').push({ type: 'snapshot', snapshot: serverSnapshot({ revision: 2, state: 'Review', dueAt: due }) });
        await flushMicrotasks();

        void controller.send('Extend');
        await flushMicrotasks();
        server.events[0].resolve({ kind: 'ok', snapshot: serverSnapshot({ revision: 3, state: 'Review', dueAt: null }) });
        await flushMicrotasks();

        expect(controller.get().dueAt).toBeNull();
        expect(server.gets).toHaveLength(1);
      });

      it('an answer that enters it with a null due time shows none and reads nothing', async () => {
        const { server, controller } = await readyReminder();

        void controller.send('Open');
        await flushMicrotasks();
        server.events[0].resolve({ kind: 'ok', snapshot: serverSnapshot({ revision: 2, state: 'Review', dueAt: null }) });
        await flushMicrotasks();

        expect(controller.get().snapshot?.state).toBe('Review');
        expect(controller.get().dueAt).toBeNull();
        expect(server.gets).toHaveLength(1);
      });

      it('a state with no timer answered with null reads nothing', async () => {
        const { server, controller } = await readyReminder();
        server.stream('r-1').push({ type: 'snapshot', snapshot: serverSnapshot({ revision: 2, state: 'Review', dueAt: due }) });
        await flushMicrotasks();

        void controller.send('Approve');
        await flushMicrotasks();
        server.events[0].resolve({ kind: 'ok', snapshot: serverSnapshot({ revision: 3, state: 'Done', dueAt: null }) });
        await flushMicrotasks();

        expect(controller.get().dueAt).toBeNull();
        expect(server.gets).toHaveLength(1);
      });
    });

    it('a reload takes the due time the read answers', async () => {
      const { server, controller } = await ready();
      server.stream('i-1').push({ type: 'reconnected' });
      await flushMicrotasks();
      server.gets[1].resolve({ kind: 'ok', snapshot: serverSnapshot({ revision: 4, ...unlocked('q'), dueAt: due }) });
      await flushMicrotasks();
      expect(controller.get().dueAt).toBe(due);
    });
  });

  describe('a machine with a custom invariant', () => {
    // A cart whose total only the server can price: the vocabulary has no arithmetic, so Cart requires a custom rule
    // the twin has no handler for, beside a built-in one.
    interface PricedSpec {
      states: {
        Cart: { items: string[]; total: number; receipt: string | null };
        Paid: { items: string[]; total: number; receipt: string | null };
      };
      triggers: { AddItem: { item: string }; Pay: undefined };
    }
    const fields = [
      { name: 'items', type: 'array', nullable: false, values: { type: 'string', nullable: false }, constraints: [] },
      { name: 'total', type: 'integer', nullable: false, constraints: [] },
      { name: 'receipt', type: 'string', nullable: true, constraints: [] },
    ];
    const priced = typedMachineFromIr<PricedSpec>({
      irFormat: 1,
      id: 'priced',
      version: 1,
      initialState: 'Cart',
      initialContext: { items: [], total: 0, receipt: null },
      states: ['Cart', 'Paid'],
      triggers: ['AddItem', 'Pay'],
      committedStates: ['Paid'],
      context: { Cart: { fields }, Paid: { fields } },
      inputs: { AddItem: { fields: [{ name: 'item', type: 'string', nullable: false, constraints: [] }] } },
      invariants: {
        Cart: {
          rule: 'all',
          rules: [
            { rule: 'absent', source: 'context', field: 'receipt' },
            { rule: 'custom', name: 'priced:total-matches-items' },
          ],
        },
      },
      transitions: [
        { from: 'Cart', trigger: 'AddItem', to: 'Cart', reduce: { reduce: 'custom', name: 'priced:add-item' } },
        { from: 'Cart', trigger: 'Pay', to: 'Paid' },
      ],
    } as IrDocument);

    const cart = { items: ['apple'], total: 120, receipt: null };

    async function open(snapshot: Parameters<FakeMachineServer['gets'][number]['resolve']>[0]) {
      const server = new FakeMachineServer();
      const controller = new ServerMachineController<PricedSpec>({
        machine: priced,
        server,
        id: 'c-1',
        newRequestId: sequentialRequestIds(),
      });
      const started = controller.start();
      await flushMicrotasks();
      server.gets[0].resolve(snapshot);
      await started;
      await flushMicrotasks();
      return { server, controller };
    }

    it('reads a server snapshot under a custom invariant', async () => {
      const { controller } = await open({ kind: 'ok', snapshot: serverSnapshot({ state: 'Cart', context: cart }) });

      expect(controller.get().status).toBe('ready');
      expect(controller.get().error).toBeNull();
      expect(controller.get().snapshot).toEqual(priced.snapshotAt('Cart', cart));
    });

    it('still refuses a server snapshot that breaks a built-in rule beside it', async () => {
      const { controller } = await open({
        kind: 'ok',
        snapshot: serverSnapshot({ state: 'Cart', context: { ...cart, receipt: 'r-1' } }),
      });

      expect(controller.get().status).toBe('stopped');
      expect(controller.get().error?.code).toBe('CLIENT_OUTDATED');
    });

    it('still refuses a server snapshot of the wrong shape', async () => {
      const { controller } = await open({
        kind: 'ok',
        snapshot: serverSnapshot({ state: 'Cart', context: { items: ['apple'], total: 'many', receipt: null } }),
      });

      expect(controller.get().status).toBe('stopped');
      expect(controller.get().error?.code).toBe('CLIENT_OUTDATED');
    });

    it('does not predict a transition into a state whose custom rule it cannot check', async () => {
      const { server, controller } = await open({ kind: 'ok', snapshot: serverSnapshot({ state: 'Cart', context: cart }) });

      void controller.send('AddItem', { item: 'pear' });
      await flushMicrotasks();

      expect(server.events).toHaveLength(1);
      expect(server.events[0].request.clientResult).toBeUndefined();
      expect(controller.get().snapshot).toEqual(priced.snapshotAt('Cart', cart));
    });
  });
});
