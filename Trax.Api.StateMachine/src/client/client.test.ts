import { describe, expect, it, vi } from 'vitest';

import { checkout } from '../machines/checkout/checkout';
import { createDraftSession, TRANSPORT_ERROR } from './draftSession';
import {
  createHttpExecutor,
  createSnapshotClient,
  type SnapshotExecutor,
  type SnapshotOutput,
} from './transport';

// A recording executor: the client is machine-agnostic, so what these assert is the SHAPE of the input
// it hands the server, not any particular machine's behavior.
const recorder = (output: SnapshotOutput = { snapshot: null, problem: null }) => {
  const calls: { operation: string; input: Record<string, unknown> }[] = [];
  const execute: SnapshotExecutor = async (operation, input) => {
    calls.push({ operation, input });
    return output;
  };
  return { calls, execute };
};

describe('createSnapshotClient', () => {
  it('sends the machine discriminator and draft id on every operation', async () => {
    const { calls, execute } = recorder();
    const client = createSnapshotClient(execute);

    await client.save('checkout', 'draft-1', '{}', 'hash');
    await client.advance('checkout', 'draft-1', 'Next');
    await client.load('checkout', 'draft-1');
    await client.send('checkout', 'draft-1');

    expect(calls.map((c) => c.operation)).toEqual([
      'saveSnapshot',
      'advanceSnapshot',
      'loadSnapshot',
      'sendSnapshot',
    ]);
    for (const call of calls) {
      expect(call.input.machine).toBe('checkout');
      expect(call.input.id).toBe('draft-1');
    }
  });

  it('threads the version-skew and divergence fields through advance', async () => {
    const { calls, execute } = recorder();
    await createSnapshotClient(execute).advance('checkout', 'draft-1', 'Pay', {
      input: '{"amount":1}',
      requestId: 'req-1',
      schemaHash: 'abc',
      clientResult: '{"state":"Paid"}',
    });

    expect(calls[0].input).toMatchObject({
      trigger: 'Pay',
      input: '{"amount":1}',
      requestId: 'req-1',
      schemaHash: 'abc',
      clientResult: '{"state":"Paid"}',
    });
  });

  it('degrades a null executor result to an empty output rather than throwing', async () => {
    const client = createSnapshotClient(async () => null);
    await expect(client.load('checkout', 'draft-1')).resolves.toEqual({
      snapshot: null,
      problem: null,
    });
  });
});

describe('createHttpExecutor', () => {
  it('posts the operation under dispatch.stateMachine and unwraps its output', async () => {
    const fetchMock = vi.fn(async () => ({
      json: async () => ({
        data: { dispatch: { stateMachine: { saveSnapshot: { output: { snapshot: '{}', problem: null } } } } },
      }),
    })) as unknown as typeof globalThis.fetch;

    const execute = createHttpExecutor('/graphql', {
      fetch: fetchMock,
      headers: () => ({ 'X-Api-Key': 'k' }),
    });
    const output = await execute('saveSnapshot', { machine: 'checkout', id: 'd' });

    expect(output).toEqual({ snapshot: '{}', problem: null });
    const [, init] = (fetchMock as unknown as { mock: { calls: [string, RequestInit][] } }).mock.calls[0];
    expect((init.headers as Record<string, string>)['X-Api-Key']).toBe('k');
    expect(JSON.parse(init.body as string).query).toContain('saveSnapshot(input:$i)');
  });

  it('throws on a GraphQL error, which arrives at HTTP 200 (an auth refusal looks like this)', async () => {
    const fetchMock = vi.fn(async () => ({
      json: async () => ({ errors: [{ message: 'not authorized' }] }),
    })) as unknown as typeof globalThis.fetch;

    const execute = createHttpExecutor('/graphql', { fetch: fetchMock });
    await expect(execute('loadSnapshot', {})).rejects.toThrow('not authorized');
  });
});

describe('createDraftSession', () => {
  const session = (execute: SnapshotExecutor, messages?: Record<string, string>) =>
    createDraftSession({
      client: createSnapshotClient(execute),
      machine: checkout,
      id: 'draft-1',
      messages,
    });

  it('takes the machine name and schema hash from the machine itself', async () => {
    const { calls, execute } = recorder();
    const s = session(execute);

    expect(s.machine).toBe(checkout.id);
    await s.save('{}');
    expect(calls[0].input.machine).toBe(checkout.id);
    expect(calls[0].input.schemaHash).toBe(checkout.schemaHash);
  });

  it('returns ok with the stored snapshot', async () => {
    const { execute } = recorder({ snapshot: '{"state":"Cart"}', problem: null });
    await expect(session(execute).load()).resolves.toEqual({
      ok: true,
      snapshot: '{"state":"Cart"}',
    });
  });

  it('reports "no draft yet" as ok with a null snapshot, not as a failure', async () => {
    const { execute } = recorder({ snapshot: null, problem: null });
    await expect(session(execute).load()).resolves.toEqual({ ok: true, snapshot: null });
  });

  it('surfaces a typed problem, preferring the server message over the fallback', async () => {
    const { execute } = recorder({
      snapshot: null,
      problem: { code: 'draft-committed', message: 'Already completed.' },
    });
    await expect(session(execute).save('{}')).resolves.toEqual({
      ok: false,
      code: 'draft-committed',
      message: 'Already completed.',
    });
  });

  it('falls back to the configured copy when the server refuses without a message', async () => {
    const { execute } = recorder({ snapshot: null, problem: { code: 'guard-failed', message: '' } });
    const result = await session(execute, { sendRefused: 'The letter could not be sent.' }).send('r');
    expect(result).toEqual({
      ok: false,
      code: 'guard-failed',
      message: 'The letter could not be sent.',
    });
  });

  it('never throws: an unreachable server becomes a transport-error result', async () => {
    const execute: SnapshotExecutor = async () => {
      throw new Error('offline');
    };
    await expect(session(execute).save('{}')).resolves.toMatchObject({
      ok: false,
      code: TRANSPORT_ERROR,
    });
  });

  it('accepts a raw machine name with an explicit schema hash', async () => {
    const { calls, execute } = recorder();
    const s = createDraftSession({
      client: createSnapshotClient(execute),
      machine: 'turnstile',
      id: 'd',
      schemaHash: 'hash-1',
    });
    await s.send('req');
    expect(calls[0].input).toMatchObject({ machine: 'turnstile', schemaHash: 'hash-1' });
  });
});
