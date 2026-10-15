// @vitest-environment jsdom
import { act, cleanup, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { turnstile } from '../machines/turnstile/turnstile';
import { FakeMachineServer, flushMicrotasks, serverSnapshot } from './testing/FakeMachineServer';
import { useRememberedMachine } from './useRememberedMachine';

// The draft a page keeps across visits: the instance id in this browser, a start under a kept requestId until it
// answers, and a new instance once the server no longer holds the old one.

// A localStorage of its own: the runtime's global can shadow jsdom's with one that holds nothing.
class MemoryStorage {
  private readonly items = new Map<string, string>();
  get length() {
    return this.items.size;
  }
  clear() {
    this.items.clear();
  }
  getItem(key: string) {
    return this.items.get(key) ?? null;
  }
  key(index: number) {
    return [...this.items.keys()][index] ?? null;
  }
  removeItem(key: string) {
    this.items.delete(key);
  }
  setItem(key: string, value: string) {
    this.items.set(key, String(value));
  }
}

beforeEach(() => {
  Object.defineProperty(globalThis, 'localStorage', { value: new MemoryStorage(), configurable: true });
});

afterEach(() => {
  cleanup();
  sessionStorage.clear();
});

const KEY = 'test.turnstile';
let minted = 0;
const newRequestId = () => `minted-${++minted}`;

function Page(props: { server: FakeMachineServer; rememberKey?: string }) {
  const m = useRememberedMachine(turnstile, { server: props.server, rememberKey: props.rememberKey ?? KEY, newRequestId });
  return (
    <div>
      <p data-testid="id">{m.id ?? ''}</p>
      <p data-testid="state">{m.state ?? ''}</p>
      <button onClick={() => m.forget()}>forget</button>
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

describe('useRememberedMachine', () => {
  it('starts a new instance and remembers its id', async () => {
    const server = new FakeMachineServer();
    render(<Page server={server} />);
    await settle();
    await settle(() => server.starts[0].resolve({ kind: 'started', id: 'i-1', snapshot: serverSnapshot() }));

    expect(text('id')).toBe('i-1');
    expect(localStorage.getItem(KEY)).toBe('i-1');
    expect(localStorage.getItem(`${KEY}:start`)).toBeNull();
  });

  it('opens the remembered instance on the next visit, starting nothing', async () => {
    localStorage.setItem(KEY, 'i-7');
    const server = new FakeMachineServer();
    render(<Page server={server} />);
    await settle();
    await settle(() => server.gets[0].resolve({ kind: 'ok', snapshot: serverSnapshot() }));

    expect(server.starts).toHaveLength(0);
    expect(server.gets[0].request.id).toBe('i-7');
    expect(text('state')).toBe('Locked');
  });

  it('sends a start that never answered again under its first requestId', async () => {
    const server = new FakeMachineServer();
    const first = render(<Page server={server} />);
    await settle();
    first.unmount();

    render(<Page server={server} />);
    await settle();
    expect(server.starts.map((s) => s.request.requestId)).toEqual([
      server.starts[0].request.requestId,
      server.starts[0].request.requestId,
    ]);
  });

  it('forgets an instance the server no longer holds and starts a new one', async () => {
    localStorage.setItem(KEY, 'i-7');
    const server = new FakeMachineServer();
    render(<Page server={server} />);
    await settle();
    await settle(() => server.gets[0].resolve({ kind: 'ok', snapshot: serverSnapshot() }));

    await settle(() => server.stream('i-7').push({ type: 'gone' }));
    await settle();

    expect(server.starts).toHaveLength(1);
    expect(localStorage.getItem(KEY)).toBeNull();
  });

  it('keeps an instance the server answers NOT_FOUND, and stops', async () => {
    localStorage.setItem(KEY, 'i-7');
    const server = new FakeMachineServer();
    render(<Page server={server} />);
    await settle();
    await settle(() => server.gets[0].resolve({ kind: 'problem', code: 'NOT_FOUND', message: 'No such instance.' }));
    await settle();

    expect(server.starts).toHaveLength(0);
    expect(localStorage.getItem(KEY)).toBe('i-7');
  });

  it('keeps an instance whose stream answers NOT_FOUND', async () => {
    localStorage.setItem(KEY, 'i-7');
    const server = new FakeMachineServer();
    render(<Page server={server} />);
    await settle();

    await settle(() => server.stream('i-7').push({ type: 'gone', notFound: true }));
    await settle();

    expect(server.starts).toHaveLength(0);
    expect(localStorage.getItem(KEY)).toBe('i-7');
  });

  it("a changed rememberKey opens that key's instance", async () => {
    localStorage.setItem('k.alice', 'i-a');
    localStorage.setItem('k.bob', 'i-b');
    const server = new FakeMachineServer();
    const page = render(<Page server={server} rememberKey="k.alice" />);
    await settle();

    page.rerender(<Page server={server} rememberKey="k.bob" />);
    await settle();
    await settle(() => screen.getByText('forget').click());

    expect(server.gets.map((g) => g.request.id)).toEqual(['i-a', 'i-b']);
    expect(localStorage.getItem('k.alice')).toBe('i-a');
    expect(localStorage.getItem('k.bob')).toBeNull();
  });

  it("another user's instance is not forgotten under a per-user key", async () => {
    localStorage.setItem('k.alice', 'i-a');
    const server = new FakeMachineServer();
    const page = render(<Page server={server} rememberKey="k.alice" />);
    await settle();
    await settle(() => server.gets[0].resolve({ kind: 'ok', snapshot: serverSnapshot() }));

    // Bob signs in on the same browser: his key holds nothing, so he starts his own and Alice's stays.
    page.rerender(<Page server={server} rememberKey="k.bob" />);
    await settle();
    await settle(() => server.starts[0].resolve({ kind: 'started', id: 'i-b', snapshot: serverSnapshot() }));

    expect(localStorage.getItem('k.bob')).toBe('i-b');
    expect(localStorage.getItem('k.alice')).toBe('i-a');

    page.rerender(<Page server={server} rememberKey="k.alice" />);
    await settle();
    expect(server.gets.at(-1)!.request.id).toBe('i-a');
    expect(server.starts).toHaveLength(1);
  });

  it('forget starts a new instance under a new requestId', async () => {
    localStorage.setItem(KEY, 'i-7');
    const server = new FakeMachineServer();
    render(<Page server={server} />);
    await settle();

    await settle(() => screen.getByText('forget').click());

    expect(server.starts).toHaveLength(1);
    expect(localStorage.getItem(KEY)).toBeNull();
    expect(localStorage.getItem(`${KEY}:start`)).toBe(server.starts[0].request.requestId);
  });
});
