// @vitest-environment jsdom
import { act, cleanup, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { turnstile, type TurnstileSpec } from '../machines/turnstile/turnstile';
import { newRequestId } from './requestId';
import { ServerMachineController } from './serverController';
import { FakeMachineServer, flushMicrotasks } from './testing/FakeMachineServer';
import { useRememberedMachine } from './useRememberedMachine';
import { useServerMachine } from './useServerMachine';

// crypto.randomUUID exists only in a secure context: a page served over http from a host that is not loopback, which
// allowInsecureTransport is for, has crypto.getRandomValues and no randomUUID. The default request id is minted there
// too, rather than start() rejecting and the hooks throwing.

const UUID_V4 = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/;

beforeEach(() => {
  const crypto = globalThis.crypto;
  vi.stubGlobal('crypto', { getRandomValues: <T extends ArrayBufferView>(array: T) => crypto.getRandomValues(array as never) as T });
  sessionStorage.clear();
});

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe('the default request id without crypto.randomUUID', () => {
  it('is a v4 UUID from getRandomValues, a new one each time', () => {
    expect(globalThis.crypto.randomUUID).toBeUndefined();

    const ids = Array.from({ length: 64 }, () => newRequestId());

    for (const id of ids) expect(id).toMatch(UUID_V4);
    expect(new Set(ids).size).toBe(ids.length);
  });

  it('is minted for a controller start', async () => {
    const server = new FakeMachineServer();
    const controller = new ServerMachineController<TurnstileSpec>({ machine: turnstile, server });

    void controller.start();
    await flushMicrotasks();

    expect(server.starts).toHaveLength(1);
    expect(server.starts[0].request.requestId).toMatch(UUID_V4);
    controller.dispose();
  });

  it('is minted for the server hook start', async () => {
    const server = new FakeMachineServer();
    function Gate() {
      const m = useServerMachine(turnstile, { server });
      return <p data-testid="status">{m.status}</p>;
    }

    render(<Gate />);
    await act(flushMicrotasks);

    expect(server.starts).toHaveLength(1);
    expect(server.starts[0].request.requestId).toMatch(UUID_V4);
    expect(screen.getByTestId('status').textContent).toBe('loading');
  });

  it('is minted for the remembered hook start', async () => {
    Object.defineProperty(globalThis, 'localStorage', { value: sessionStorage, configurable: true });
    const server = new FakeMachineServer();
    function Page() {
      const m = useRememberedMachine(turnstile, { server, rememberKey: 'test.request-id' });
      return <p data-testid="id">{m.id ?? ''}</p>;
    }

    render(<Page />);
    await act(flushMicrotasks);

    expect(server.starts).toHaveLength(1);
    expect(server.starts[0].request.requestId).toMatch(UUID_V4);
  });
});
