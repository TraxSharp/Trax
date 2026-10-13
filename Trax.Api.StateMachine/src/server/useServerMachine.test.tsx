// @vitest-environment jsdom
import { StrictMode } from 'react';
import { act, cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { turnstile } from '../machines/turnstile/turnstile';
import {
  FakeMachineServer,
  flushMicrotasks,
  sequentialRequestIds,
  serverSnapshot,
} from './testing/FakeMachineServer';
import { useServerMachine, type UseServerMachineOptions } from './useServerMachine';

// The server-authoritative hook over a FakeMachineServer: the server owns the instance and the twin only
// predicts. Red until the hook exists (its stub throws); drop `.fails` from each test as it passes.

afterEach(() => {
  cleanup();
  vi.useRealTimers();
  vi.restoreAllMocks();
  sessionStorage.clear();
});

function Gate(props: UseServerMachineOptions & { onRender?: () => void }) {
  const { onRender, ...options } = props;
  const m = useServerMachine(turnstile, options);
  onRender?.();
  const paid = (m.context as { paidWith?: string } | null)?.paidWith ?? '';
  return (
    <div>
      <p data-testid="status">{m.status}</p>
      <p data-testid="state">{m.state ?? ''}</p>
      <p data-testid="paid">{paid}</p>
      <p data-testid="due">{m.dueAt ?? ''}</p>
      <p data-testid="revision">{m.revision ?? ''}</p>
      <p data-testid="error">{m.error?.code ?? ''}</p>
      <button onClick={() => void m.send('Coin', { coin: 'quarter' })}>coin</button>
      <button onClick={() => void m.send('Push')}>push</button>
      <button onClick={() => void m.retry()}>retry</button>
      {m.problem && <p role="alert">{m.problem.code}</p>}
    </div>
  );
}

const text = (id: string) => screen.getByTestId(id).textContent;

async function settle(action: () => void = () => {}): Promise<void> {
  await act(async () => {
    action();
    await flushMicrotasks();
  });
}

/** Renders the gate over instance `i-1` and answers its initial read with Locked at revision 1. */
async function renderReady(extra: Partial<UseServerMachineOptions> & { onRender?: () => void } = {}) {
  const server = new FakeMachineServer();
  const view = render(<Gate server={server} id="i-1" newRequestId={sequentialRequestIds()} {...extra} />);
  await settle();
  await settle(() => server.gets[0].resolve({ kind: 'ok', snapshot: serverSnapshot() }));
  return { server, view };
}

describe('useServerMachine', () => {
  it("shows the twin's prediction until the server answers, then the server's snapshot", async () => {
    const { server } = await renderReady();
    expect(text('state')).toBe('Locked');

    await settle(() => screen.getByText('coin').click());
    expect(text('state')).toBe('Unlocked');
    expect(text('paid')).toBe('quarter');

    // The server's context differs from the prediction, and the server wins.
    await settle(() =>
      server.events[0].resolve({
        kind: 'ok',
        snapshot: serverSnapshot({ revision: 2, state: 'Unlocked', context: { paidWith: 'dollar' } }),
      }),
    );
    expect(text('paid')).toBe('dollar');
  });

  it('rolls the prediction back on a refusal and shows the problem', async () => {
    const { server } = await renderReady();

    await settle(() => screen.getByText('coin').click());
    expect(text('state')).toBe('Unlocked');
    await settle(() => server.events[0].resolve({ kind: 'problem', code: 'guard-failed', message: 'no' }));

    expect(text('state')).toBe('Locked');
    expect(screen.getByRole('alert').textContent).toBe('guard-failed');
  });

  it('gone stops the hook, disables events and does not reload', async () => {
    const { server } = await renderReady();

    await settle(() => server.stream('i-1').push({ type: 'gone' }));
    expect(text('status')).toBe('stopped');
    expect(text('error')).toBe('gone');

    await settle(() => screen.getByText('coin').click());
    expect(server.events).toHaveLength(0);
    expect(server.gets).toHaveLength(1);
    expect(server.openSubscriptions()).toHaveLength(0);
  });

  it('client-outdated stops the hook with a typed error and makes no further requests', async () => {
    // From a mutation's answer.
    {
      const { server, view } = await renderReady();
      await settle(() => screen.getByText('coin').click());
      await settle(() =>
        server.events[0].resolve({ kind: 'problem', code: 'client-outdated', message: 'reload' }),
      );
      expect(text('status')).toBe('stopped');
      expect(text('error')).toBe('client-outdated');
      await settle(() => screen.getByText('push').click());
      expect(server.log.filter((op) => op !== 'unsubscribe')).toEqual(['subscribe', 'get', 'event']);
      view.unmount();
    }
    // From the stream's end.
    {
      const { server } = await renderReady();
      await settle(() => server.stream('i-1').push({ type: 'client-outdated' }));
      expect(text('status')).toBe('stopped');
      expect(text('error')).toBe('client-outdated');
      await settle(() => screen.getByText('coin').click());
      expect(server.log.filter((op) => op !== 'unsubscribe')).toEqual(['subscribe', 'get']);
    }
  });

  it(
    'unmount mid-request: no state update after unmount, one unsubscribe, the late result dropped',
    async () => {
      const onRender = vi.fn();
      const { server, view } = await renderReady({ onRender });
      const errors = vi.spyOn(console, 'error').mockImplementation(() => {});

      await settle(() => screen.getByText('coin').click());
      view.unmount();
      const renders = onRender.mock.calls.length;

      await settle(() =>
        server.events[0].resolve({
          kind: 'ok',
          snapshot: serverSnapshot({ revision: 2, state: 'Unlocked', context: { paidWith: 'quarter' } }),
        }),
      );

      expect(onRender).toHaveBeenCalledTimes(renders);
      expect(server.subscriptions).toHaveLength(1);
      expect(server.subscriptions[0].closeCount).toBe(1);
      expect(server.gets).toHaveLength(1);
      expect(errors).not.toHaveBeenCalled();
      errors.mockRestore();
    },
  );

  it('StrictMode: start runs under one requestId and one live subscription remains', async () => {
    const server = new FakeMachineServer();
    render(
      <StrictMode>
        <Gate server={server} startInput={{}} newRequestId={sequentialRequestIds()} />
      </StrictMode>,
    );
    await settle();

    expect(server.starts.length).toBeGreaterThanOrEqual(1);
    expect(server.starts.length).toBeLessThanOrEqual(2);
    expect(new Set(server.starts.map((s) => s.request.requestId)).size).toBe(1);

    // A retried start with the same requestId answers with the same instance.
    await settle(() => {
      for (const start of server.starts)
        start.resolve({ kind: 'started', id: 'i-1', snapshot: serverSnapshot() });
    });

    expect(server.openSubscriptions()).toHaveLength(1);
    expect(server.openSubscriptions('i-1')).toHaveLength(1);
    expect(text('state')).toBe('Locked');
  });

  it('starts under a given startRequestId, and a new one starts a new instance', async () => {
    const server = new FakeMachineServer();
    const view = render(<Gate server={server} startRequestId="kept-1" newRequestId={sequentialRequestIds()} />);
    await settle();
    expect(server.starts.map((s) => s.request.requestId)).toEqual(['kept-1']);
    await settle(() => server.starts[0].resolve({ kind: 'started', id: 'i-1', snapshot: serverSnapshot() }));
    expect(text('state')).toBe('Locked');
    expect(text('revision')).toBe('1');

    // The same id again (a re-render, or a reload that kept it) starts nothing new.
    view.rerender(<Gate server={server} startRequestId="kept-1" newRequestId={sequentialRequestIds()} />);
    await settle();
    expect(server.starts).toHaveLength(1);

    view.rerender(<Gate server={server} startRequestId="kept-2" newRequestId={sequentialRequestIds()} />);
    await settle();
    expect(server.starts.map((s) => s.request.requestId)).toEqual(['kept-1', 'kept-2']);
    expect(server.openSubscriptions('i-1')).toHaveLength(0);
  });

  it('retry resends a start that failed in transport under its requestId: one instance', async () => {
    const server = new FakeMachineServer();
    render(<Gate server={server} startInput={{}} newRequestId={sequentialRequestIds()} />);
    await settle();
    await settle(() => server.starts[0].fail());
    expect(text('status')).toBe('loading');
    expect(screen.getByRole('alert').textContent).toBe('transport-error');

    await settle(() => screen.getByText('retry').click());
    expect(server.starts).toHaveLength(2);
    expect(server.starts[1].request.requestId).toBe(server.starts[0].request.requestId);
    await settle(() => server.starts[1].resolve({ kind: 'started', id: 'i-1', snapshot: serverSnapshot() }));

    expect(text('status')).toBe('ready');
    expect(text('state')).toBe('Locked');
    expect(screen.queryByRole('alert')).toBeNull();
    expect(server.openSubscriptions()).toHaveLength(1);
  });

  it('an event retried after a transport error carries its first requestId', async () => {
    const { server } = await renderReady();
    await settle(() => screen.getByText('coin').click());
    await settle(() => server.events[0].fail());
    expect(text('state')).toBe('Locked');

    await settle(() => screen.getByText('retry').click());
    expect(server.events).toHaveLength(2);
    expect(server.events[1].request).toEqual(server.events[0].request);
    await settle(() =>
      server.events[1].resolve({
        kind: 'ok',
        snapshot: serverSnapshot({ revision: 2, state: 'Unlocked', context: { paidWith: 'quarter' } }),
      }),
    );
    expect(text('state')).toBe('Unlocked');
    expect(screen.queryByRole('alert')).toBeNull();
  });

  it('keeps an unanswered start requestId across a reload, and forgets it once the start answers', async () => {
    const server = new FakeMachineServer();
    const first = render(<Gate server={server} startInput={{}} newRequestId={() => 'minted-1'} />);
    await settle();
    await settle(() => server.starts[0].fail());
    first.unmount();

    // The reload: a new page, a new hook, a fresh requestId minter.
    render(<Gate server={server} startInput={{}} newRequestId={() => 'minted-2'} />);
    await settle();
    expect(server.starts.map((s) => s.request.requestId)).toEqual(['minted-1', 'minted-1']);
    await settle(() => server.starts[1].resolve({ kind: 'started', id: 'i-1', snapshot: serverSnapshot() }));
    expect(text('state')).toBe('Locked');
    expect(sessionStorage.length).toBe(0);
  });

  it('forgets a start requestId the server refused, so the next page starts anew', async () => {
    const server = new FakeMachineServer();
    const first = render(<Gate server={server} startInput={{}} newRequestId={() => 'minted-1'} />);
    await settle();
    await settle(() => server.starts[0].resolve({ kind: 'problem', code: 'too-many-drafts', message: 'cap' }));
    first.unmount();

    render(<Gate server={server} startInput={{}} newRequestId={() => 'minted-2'} />);
    await settle();
    expect(server.starts.map((s) => s.request.requestId)).toEqual(['minted-1', 'minted-2']);
  });

  it('starts when sessionStorage refuses every access, and with startStorageKey null keeps nothing', async () => {
    const server = new FakeMachineServer();
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('denied');
    });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('denied');
    });
    render(<Gate server={server} startInput={{}} newRequestId={() => 'minted-1'} />);
    await settle();
    await settle(() => server.starts[0].resolve({ kind: 'started', id: 'i-1', snapshot: serverSnapshot() }));
    expect(text('state')).toBe('Locked');
    vi.restoreAllMocks();
    cleanup();

    const other = new FakeMachineServer();
    render(<Gate server={other} startInput={{}} startStorageKey={null} newRequestId={() => 'minted-2'} />);
    await settle();
    expect(other.starts).toHaveLength(1);
    expect(sessionStorage.length).toBe(0);
  });

  it('changing the instance id closes the old stream and ignores its late events', async () => {
    const { server, view } = await renderReady();
    const old = server.stream('i-1');

    view.rerender(<Gate server={server} id="i-2" newRequestId={sequentialRequestIds()} />);
    await settle();
    expect(old.open).toBe(false);
    expect(server.openSubscriptions('i-2')).toHaveLength(1);

    await settle(() =>
      server.gets[1].resolve({ kind: 'ok', snapshot: serverSnapshot({ rowId: 'row-2', revision: 1 }) }),
    );
    await settle(() =>
      old.pushLate({
        type: 'snapshot',
        snapshot: serverSnapshot({ revision: 9, state: 'Unlocked', context: { paidWith: 'quarter' } }),
      }),
    );

    expect(text('state')).toBe('Locked');
    expect(text('paid')).toBe('');
  });

  it('shows dueAt from beside the snapshot and never fires a timer', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'setInterval', 'Date'] });
    vi.setSystemTime(new Date('2026-10-09T12:00:00Z'));
    const server = new FakeMachineServer();
    render(<Gate server={server} id="i-1" newRequestId={sequentialRequestIds()} />);
    await settle();
    await settle(() =>
      server.gets[0].resolve({
        kind: 'ok',
        snapshot: serverSnapshot({
          state: 'Unlocked',
          context: { paidWith: 'quarter' },
          dueAt: '2026-10-09T12:10:00Z',
        }),
      }),
    );
    expect(text('due')).toBe('2026-10-09T12:10:00Z');

    // Past the due time, the client does nothing: the server fires the timer.
    await settle(() => vi.advanceTimersByTime(20 * 60 * 1000));
    expect(server.log).toEqual(['subscribe', 'get']);
    expect(text('state')).toBe('Unlocked');

    // The server's push moves it.
    await settle(() =>
      server.stream('i-1').push({ type: 'snapshot', snapshot: serverSnapshot({ revision: 2, dueAt: null }) }),
    );
    expect(text('state')).toBe('Locked');
    expect(text('due')).toBe('');
  });
});
