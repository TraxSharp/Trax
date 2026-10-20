import { describe, expect, it, vi } from 'vitest';
import { FakeMachineServer, flushMicrotasks, sequentialRequestIds, serverSnapshot } from './FakeMachineServer';

// The fake is what the server-authoritative hook's tests stand on, so it is tested on its own: a red hook test
// must fail because the hook is missing, never because the fake misbehaves.

describe('FakeMachineServer', () => {
  it('records each operation in arrival order and leaves it pending', () => {
    const server = new FakeMachineServer();
    void server.start({ a: 1 }, 'rq-1');
    server.subscribe('i-1', () => {});
    void server.get('i-1');
    void server.event({ id: 'i-1', trigger: 'Coin', input: { coin: 'quarter' }, requestId: 'rq-2' });

    expect(server.log).toEqual(['start', 'subscribe', 'get', 'event']);
    expect(server.starts[0].request).toEqual({ input: { a: 1 }, requestId: 'rq-1' });
    expect(server.gets[0].request).toEqual({ id: 'i-1' });
    expect(server.events[0].request).toEqual({
      id: 'i-1',
      trigger: 'Coin',
      input: { coin: 'quarter' },
      requestId: 'rq-2',
    });
    expect([server.starts[0], server.gets[0], server.events[0]].every((c) => !c.settled)).toBe(true);
  });

  it('resolves a call with the answer the test gives', async () => {
    const server = new FakeMachineServer();
    const answer = vi.fn();
    void server.get('i-1').then(answer);
    await flushMicrotasks();
    expect(answer).not.toHaveBeenCalled();

    server.gets[0].resolve({ kind: 'ok', snapshot: serverSnapshot({ revision: 4 }) });
    await flushMicrotasks();

    expect(answer).toHaveBeenCalledWith({ kind: 'ok', snapshot: serverSnapshot({ revision: 4 }) });
    expect(server.gets[0].settled).toBe(true);
  });

  it('fails a call as a rejected promise, like an unreachable server', async () => {
    const server = new FakeMachineServer();
    const result = server.event({ id: 'i-1', trigger: 'Push', requestId: 'rq-1' });
    server.events[0].fail();

    await expect(result).rejects.toThrow('transport failed');
  });

  it('refuses to settle a call twice', () => {
    const server = new FakeMachineServer();
    void server.get('i-1').catch(() => {});
    server.gets[0].resolve({ kind: 'problem', code: 'GONE', message: 'GONE' });

    expect(() => server.gets[0].resolve({ kind: 'problem', code: 'GONE', message: 'GONE' })).toThrow(
      'already resolved',
    );
    expect(() => server.gets[0].fail()).toThrow('already resolved');
  });

  it('delivers stream messages to the listener, and counts every close', () => {
    const server = new FakeMachineServer();
    const heard = vi.fn();
    const close = server.subscribe('i-1', heard);

    server.stream('i-1').push({ type: 'reconnected' });
    expect(heard).toHaveBeenCalledWith({ type: 'reconnected' });

    close();
    close();
    expect(server.subscriptions[0].open).toBe(false);
    expect(server.subscriptions[0].closeCount).toBe(2);
    expect(server.log).toEqual(['subscribe', 'unsubscribe', 'unsubscribe']);
  });

  it('refuses an ordinary push on a closed stream, but delivers a late one', () => {
    const server = new FakeMachineServer();
    const heard = vi.fn();
    const close = server.subscribe('i-1', heard);
    const subscription = server.subscriptions[0];
    close();

    expect(() => subscription.push({ type: 'gone' })).toThrow('closed');
    expect(heard).not.toHaveBeenCalled();

    subscription.pushLate({ type: 'gone' });
    expect(heard).toHaveBeenCalledWith({ type: 'gone' });
  });

  it('finds the open subscriptions of one instance, and insists on exactly one for stream()', () => {
    const server = new FakeMachineServer();
    const closeA = server.subscribe('i-1', () => {});
    server.subscribe('i-2', () => {});

    expect(server.openSubscriptions().map((s) => s.id)).toEqual(['i-1', 'i-2']);
    expect(server.openSubscriptions('i-2')).toHaveLength(1);
    expect(() => server.stream()).toThrow('found 2');

    closeA();
    expect(server.stream().id).toBe('i-2');
    expect(() => server.stream('i-1')).toThrow('found 0');
  });

  it('builds snapshots with defaults and mints distinct request ids', () => {
    expect(serverSnapshot()).toEqual({ rowId: 'row-1', revision: 1, state: 'Locked', context: {} });
    expect(serverSnapshot({ state: 'Unlocked', context: { paidWith: 'quarter' }, dueAt: 'x' })).toEqual({
      rowId: 'row-1',
      revision: 1,
      state: 'Unlocked',
      context: { paidWith: 'quarter' },
      dueAt: 'x',
    });
    const next = sequentialRequestIds();
    expect([next(), next(), next()]).toEqual(['rq-1', 'rq-2', 'rq-3']);
  });

  it('lets continuations run under fake timers too', async () => {
    vi.useFakeTimers();
    try {
      const server = new FakeMachineServer();
      const answer = vi.fn();
      void server.get('i-1').then(answer);
      server.gets[0].resolve({ kind: 'ok', snapshot: serverSnapshot() });
      await flushMicrotasks();
      expect(answer).toHaveBeenCalledOnce();
    } finally {
      vi.useRealTimers();
    }
  });
});
