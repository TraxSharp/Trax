import { afterEach, describe, expect, it, vi } from 'vitest';

import { turnstile, type TurnstileSpec } from '../machines/turnstile/turnstile';
import { ServerMachineController, type StreamMessage } from '../server/serverController';
import { flushMicrotasks, sequentialRequestIds } from '../server/testing/FakeMachineServer';
import {
  createMachineServer,
  persistedMessageReplacer,
  type PersistedSubscriptionSocket,
  type SubscriptionSink,
} from './machineServer';
import type { MachineDocument } from './machines';

// createMachineServer over a fake fetch and a fake graphql-ws socket: each operation goes by its persisted id with
// its values as variables, and the change stream is subscribed by id with an empty document.

afterEach(() => {
  vi.useRealTimers();
});

const hash = turnstile.schemaHash;

const doc = (operation: string, variables: string): MachineDocument => ({
  id: `trax_machine_turnstile_${operation}_${hash}`,
  machine: 'turnstile',
  operation,
  irHash: hash,
  document: `op(${variables})`,
});

const DOCUMENTS: MachineDocument[] = [
  doc('start', '$requestId: String!'),
  doc('discard', '$id: UUID!, $requestId: String!'),
  doc('coin', '$id: UUID!, $input: CoinInput!, $requestId: String!, $clientResult: String'),
  doc('push', '$id: UUID!, $requestId: String!, $clientResult: String'),
  doc('get', '$id: UUID!'),
  doc('machineChanged', '$id: UUID!'),
];

interface Sent {
  id: string;
  variables: Record<string, unknown>;
}

// A fetch that records each request and answers with the next canned body.
const http = (...bodies: unknown[]) => {
  const sent: Sent[] = [];
  const fetch = (async (_url: string, init: { body: string }) => {
    sent.push(JSON.parse(init.body) as Sent);
    return { json: async () => bodies.shift() };
  }) as unknown as typeof globalThis.fetch;
  return { sent, fetch };
};

const answer = (field: string, value: unknown) => ({ data: { machines: { turnstile: { [field]: value } } } });

class FakeSocket implements PersistedSubscriptionSocket {
  readonly subscriptions: { payload: { query: string; id: string; variables: Record<string, unknown> }; sink: SubscriptionSink; closed: boolean }[] =
    [];

  subscribe(payload: { query: string; id: string; variables: Record<string, unknown> }, sink: SubscriptionSink) {
    const subscription = { payload, sink, closed: false };
    this.subscriptions.push(subscription);
    return () => {
      subscription.closed = true;
    };
  }

  get last() {
    return this.subscriptions[this.subscriptions.length - 1];
  }

  push(change: Record<string, unknown>) {
    this.last.sink.next({ data: { machineChanged: { reload: false, gone: false, problem: null, ...change } } });
  }
}

const serverOver = (fetch: typeof globalThis.fetch, socket = new FakeSocket(), resubscribeDelays?: number[]) => ({
  socket,
  server: createMachineServer('/graphql', {
    fetch,
    machine: turnstile,
    documents: DOCUMENTS,
    socket,
    resubscribeDelays,
  }),
});

describe('createMachineServer operations', () => {
  it('starts by the start document, and answers the snapshot beside its revision and due time', async () => {
    const { sent, fetch } = http(
      answer('start', { id: 'i-1', revision: 1, state: 'Locked', context: { paidWith: null }, dueAt: null, problem: null }),
    );
    const { server } = serverOver(fetch);

    const started = await server.start(undefined, 'rq-start');

    expect(sent).toEqual([{ id: `trax_machine_turnstile_start_${hash}`, variables: { requestId: 'rq-start' } }]);
    expect(started).toEqual({
      kind: 'started',
      id: 'i-1',
      snapshot: { revision: 1, state: 'Locked', context: {}, dueAt: null },
    });
  });

  it('sends an event by its own document, with its input, requestId and client result as variables', async () => {
    const { sent, fetch } = http(
      answer('coin', {
        id: 'i-1',
        revision: 2,
        state: 'Unlocked',
        context: { paidWith: 'quarter' },
        dueAt: '2026-10-12T10:00:00Z',
        problem: null,
      }),
    );
    const { server } = serverOver(fetch);

    const answered = await server.event({
      id: 'i-1',
      trigger: 'Coin',
      input: { coin: 'quarter' },
      requestId: 'rq-1',
      clientResult: '{"state":"Unlocked"}',
    });

    expect(sent).toEqual([
      {
        id: `trax_machine_turnstile_coin_${hash}`,
        variables: { id: 'i-1', input: { coin: 'quarter' }, requestId: 'rq-1', clientResult: '{"state":"Unlocked"}' },
      },
    ]);
    expect(answered).toEqual({
      kind: 'ok',
      snapshot: { revision: 2, state: 'Unlocked', context: { paidWith: 'quarter' }, dueAt: '2026-10-12T10:00:00Z' },
    });
  });

  it('leaves the due time out of a replay that cannot know it, and keeps a null the server sent', async () => {
    const replay = { id: 'i-1', revision: 2, state: 'Unlocked', context: { paidWith: 'quarter' }, problem: null };
    const { fetch } = http(
      answer('coin', { ...replay, dueAt: null, dueAtKnown: false }),
      answer('coin', { ...replay, dueAt: null, dueAtKnown: true }),
    );
    const { server } = serverOver(fetch);
    const request = { id: 'i-1', trigger: 'Coin', input: { coin: 'quarter' }, requestId: 'rq-1' };

    const unknown = await server.event(request);
    const cleared = await server.event(request);

    // Unknown is absent, so the controller keeps what it holds; null is the server saying there is no timer.
    expect(unknown).toEqual({ kind: 'ok', snapshot: { revision: 2, state: 'Unlocked', context: { paidWith: 'quarter' } } });
    expect((unknown as { snapshot: object }).snapshot).not.toHaveProperty('dueAt');
    expect(cleared).toEqual({
      kind: 'ok',
      snapshot: { revision: 2, state: 'Unlocked', context: { paidWith: 'quarter' }, dueAt: null },
    });
  });

  it('answers a refusal as a problem, never a rejection', async () => {
    const { fetch } = http(answer('push', { id: 'i-1', problem: { code: 'forbidden', message: 'Operator only.' } }));
    const { server } = serverOver(fetch);

    expect(await server.event({ id: 'i-1', trigger: 'Push', requestId: 'rq-1' })).toEqual({
      kind: 'problem',
      code: 'forbidden',
      message: 'Operator only.',
    });
  });

  it('answers a document of another version as a problem, and rejects any other GraphQL error', async () => {
    const { fetch } = http(
      { errors: [{ message: 'Reload.', extensions: { code: 'client-outdated' } }] },
      { errors: [{ message: 'Not authorized.' }] },
    );
    const { server } = serverOver(fetch);

    expect(await server.get('i-1')).toEqual({ kind: 'problem', code: 'client-outdated', message: 'Reload.' });
    await expect(server.get('i-1')).rejects.toThrow('Not authorized.');
  });

  it('answers a get of an instance the server does not hold as not-found', async () => {
    const { sent, fetch } = http(answer('get', null));
    const { server } = serverOver(fetch);

    expect(await server.get('i-9')).toMatchObject({ kind: 'problem', code: 'not-found' });
    expect(sent).toEqual([{ id: `trax_machine_turnstile_get_${hash}`, variables: { id: 'i-9' } }]);
  });

  it('discards by the discard document', async () => {
    const { sent, fetch } = http(answer('discard', { id: 'i-1', discarded: true, problem: null }));
    const { server } = serverOver(fetch);

    expect(await server.discard('i-1', 'rq-d')).toEqual({ kind: 'discarded' });
    expect(sent).toEqual([{ id: `trax_machine_turnstile_discard_${hash}`, variables: { id: 'i-1', requestId: 'rq-d' } }]);
  });

  it('refuses at creation a document set without the machineChanged document', () => {
    expect(() =>
      createMachineServer('/graphql', {
        fetch: http().fetch,
        machine: turnstile,
        documents: DOCUMENTS.filter((d) => d.operation !== 'machineChanged'),
        socket: new FakeSocket(),
      }),
    ).toThrow(/machineChanged/);
  });
});

describe('createMachineServer change stream', () => {
  const follow = (resubscribeDelays?: number[]) => {
    const { server, socket } = serverOver(http().fetch, new FakeSocket(), resubscribeDelays);
    const messages: StreamMessage[] = [];
    const close = server.subscribe('i-1', (m) => messages.push(m));
    return { socket, messages, close };
  };

  it('subscribes by the machineChanged id with an empty document', () => {
    const { socket } = follow();

    expect(socket.subscriptions).toHaveLength(1);
    expect(socket.last.payload).toEqual({
      query: '',
      id: `trax_machine_turnstile_machineChanged_${hash}`,
      variables: { id: 'i-1' },
    });
  });

  it('delivers each pushed snapshot with its incarnation and revision', () => {
    const { socket, messages } = follow();

    socket.push({ rowId: 7, revision: 3, state: 'Unlocked', context: { paidWith: 'quarter' }, dueAt: null });
    socket.push({ rowId: 7, revision: 4, state: 'Locked', context: { paidWith: null } });

    expect(messages).toEqual([
      {
        type: 'snapshot',
        snapshot: { rowId: '7', revision: 3, state: 'Unlocked', context: { paidWith: 'quarter' }, dueAt: null },
      },
      // An event that does not carry the due time leaves it absent, never null, which would say "no timer".
      { type: 'snapshot', snapshot: { rowId: '7', revision: 4, state: 'Locked', context: {}, dueAt: undefined } },
    ]);
    expect((messages[1] as { snapshot: object }).snapshot).toHaveProperty('dueAt', undefined);
  });

  it('ends with gone on gone and on not-found, with client-outdated on client-outdated, and reloads on reload', () => {
    const gone = follow();
    gone.socket.push({ gone: true });
    expect(gone.messages).toEqual([{ type: 'gone' }]);

    const missing = follow();
    missing.socket.push({ problem: { code: 'not-found', message: 'No instance.' } });
    expect(missing.messages).toEqual([{ type: 'gone' }]);

    const outdated = follow();
    outdated.socket.push({ problem: { code: 'client-outdated', message: 'Reload.' } });
    expect(outdated.messages).toEqual([{ type: 'client-outdated' }]);

    const reload = follow();
    reload.socket.push({ reload: true });
    expect(reload.messages).toEqual([{ type: 'reconnected' }]);
  });

  it('subscribes again after a stream that ended without saying why, and never after one that ended', () => {
    vi.useFakeTimers();
    const { socket, messages } = follow([100, 200]);

    socket.last.sink.complete();
    expect(socket.subscriptions).toHaveLength(1);
    vi.advanceTimersByTime(100);
    expect(socket.subscriptions).toHaveLength(2);

    socket.last.sink.error(new Error('socket closed'));
    vi.advanceTimersByTime(200);
    expect(socket.subscriptions).toHaveLength(3);

    // The first event of the new stream is the instance as it is then; nothing is read again.
    socket.push({ rowId: 7, revision: 5, state: 'Locked', context: {} });
    expect(messages).toEqual([
      { type: 'snapshot', snapshot: { rowId: '7', revision: 5, state: 'Locked', context: {} } },
    ]);

    socket.push({ gone: true });
    socket.last.sink.complete();
    vi.advanceTimersByTime(10_000);
    expect(socket.subscriptions).toHaveLength(3);
  });

  it('closes the socket subscription and any pending resubscription when closed', () => {
    vi.useFakeTimers();
    const { socket, close } = follow([100]);

    socket.last.sink.complete();
    close();
    vi.advanceTimersByTime(1_000);

    expect(socket.subscriptions).toHaveLength(1);
    expect(socket.last.closed).toBe(true);
  });

  it('subscribes again after a refusal to watch that is not about the instance', () => {
    vi.useFakeTimers();
    const { socket, messages } = follow([100]);

    socket.push({ problem: { code: 'too-many-subscriptions', message: 'Complete one.' } });
    vi.advanceTimersByTime(100);

    expect(messages).toEqual([]);
    expect(socket.subscriptions).toHaveLength(2);
  });
});

describe('persistedMessageReplacer', () => {
  it('leaves an empty document out of a subscribe message and keeps everything else', () => {
    const message = { type: 'subscribe', payload: { query: '', id: 'doc-1', variables: { id: 'i-1' } } };

    expect(JSON.parse(JSON.stringify(message, persistedMessageReplacer))).toEqual({
      type: 'subscribe',
      payload: { id: 'doc-1', variables: { id: 'i-1' } },
    });
    expect(JSON.stringify({ query: 'q' }, persistedMessageReplacer)).toBe('{"query":"q"}');
  });
});

describe('ServerMachineController over createMachineServer', () => {
  it('starts, follows a push and confirms an event, all by persisted id', async () => {
    const { sent, fetch } = http(
      answer('start', { id: 'i-1', revision: 1, state: 'Locked', context: {}, dueAt: null, problem: null }),
      answer('coin', { id: 'i-1', revision: 3, state: 'Unlocked', context: { paidWith: 'dollar' }, problem: null }),
    );
    const { server, socket } = serverOver(fetch);
    const controller = new ServerMachineController<TurnstileSpec>({
      machine: turnstile,
      server,
      newRequestId: sequentialRequestIds(),
    });

    await controller.start();
    expect(controller.get()).toMatchObject({ status: 'ready', id: 'i-1', revision: 1 });

    socket.push({ rowId: 7, revision: 2, state: 'Locked', context: {} });
    expect(controller.get()).toMatchObject({ rowId: '7', revision: 2 });

    const outcome = await controller.send('Coin', { coin: 'dollar' });
    await flushMicrotasks();

    expect(outcome.outcome).toBe('confirmed');
    expect(controller.get().snapshot).toEqual(turnstile.snapshotAt('Unlocked', { paidWith: 'dollar' }));
    expect(controller.get().revision).toBe(3);
    expect(sent.map((s) => s.id)).toEqual([
      `trax_machine_turnstile_start_${hash}`,
      `trax_machine_turnstile_coin_${hash}`,
    ]);
    expect(sent[1].variables.clientResult).toBe(
      turnstile.serialize(turnstile.snapshotAt('Unlocked', { paidWith: 'dollar' })),
    );
    controller.dispose();
    expect(socket.last.closed).toBe(true);
  });
});
