import { describe, expect, it } from 'vitest';

import { checkout } from '../machines/checkout/checkout';
import { createDraftSession, startDraft, TRANSPORT_ERROR } from './draftSession';
import {
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

  it('starts with the machine and request id, and discards by id and request id', async () => {
    const { calls, execute } = recorder();
    const client = createSnapshotClient(execute);

    await client.start('checkout', 'start-1', { input: '{"items":[]}', schemaHash: 'hash' });
    await client.discard('checkout', 'draft-1', 'discard-1');

    expect(calls).toEqual([
      {
        operation: 'startSnapshot',
        input: { machine: 'checkout', requestId: 'start-1', input: '{"items":[]}', schemaHash: 'hash' },
      },
      {
        operation: 'discardSnapshot',
        input: { machine: 'checkout', id: 'draft-1', requestId: 'discard-1' },
      },
    ]);
  });

  it('degrades a null executor result to an empty output rather than throwing', async () => {
    const client = createSnapshotClient(async () => null);
    await expect(client.load('checkout', 'draft-1')).resolves.toEqual({
      snapshot: null,
      problem: null,
    });
  });
});

describe('createDraftSession', () => {
  const session = (execute: SnapshotExecutor, messages?: Record<string, string>) =>
    createDraftSession({
      executor: execute,
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

  it('reads the server\'s not-found answer to a load as "no draft yet"', async () => {
    // The server answers a draft that does not exist yet with this problem; a caller that saves the
    // first step when there is no draft must see ok with a null snapshot, not a failure.
    const { execute } = recorder({
      snapshot: null,
      problem: { code: 'NOT_FOUND', message: 'No draft to resume.' },
    });
    await expect(session(execute).load()).resolves.toEqual({ ok: true, snapshot: null });
  });

  it('surfaces a typed problem, preferring the server message over the fallback', async () => {
    const { execute } = recorder({
      snapshot: null,
      problem: { code: 'DRAFT_COMMITTED', message: 'Already completed.' },
    });
    await expect(session(execute).save('{}')).resolves.toEqual({
      ok: false,
      code: 'DRAFT_COMMITTED',
      message: 'Already completed.',
    });
  });

  it('falls back to the configured copy when the server refuses without a message', async () => {
    const { execute } = recorder({ snapshot: null, problem: { code: 'GUARD_FAILED', message: '' } });
    const result = await session(execute, { sendRefused: 'The letter could not be sent.' }).send('r');
    expect(result).toEqual({
      ok: false,
      code: 'GUARD_FAILED',
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
      executor: execute,
      machine: 'turnstile',
      id: 'd',
      schemaHash: 'hash-1',
    });
    await s.send('req');
    expect(calls[0].input).toMatchObject({ machine: 'turnstile', schemaHash: 'hash-1' });
  });
});

describe('startDraft', () => {
  it('returns the server-minted id and the first snapshot', async () => {
    const { calls, execute } = recorder({ id: 'minted', created: true, snapshot: '{"state":"Cart"}', problem: null });

    const started = await startDraft({ executor: execute, machine: checkout, requestId: 'r-1' });

    expect(started).toEqual({ ok: true, id: 'minted', snapshot: '{"state":"Cart"}', created: true });
    expect(calls[0].input).toMatchObject({ machine: checkout.id, requestId: 'r-1', schemaHash: checkout.schemaHash });
  });

  it('surfaces a refusal as a typed result, and never throws', async () => {
    const refused = recorder({ snapshot: null, problem: { code: 'TOO_MANY_DRAFTS', message: 'Too many.' } });
    await expect(
      startDraft({ executor: refused.execute, machine: 'checkout', requestId: 'r' }),
    ).resolves.toEqual({ ok: false, code: 'TOO_MANY_DRAFTS', message: 'Too many.' });

    const offline: SnapshotExecutor = async () => {
      throw new Error('offline');
    };
    await expect(
      startDraft({ executor: offline, machine: 'checkout', requestId: 'r' }),
    ).resolves.toMatchObject({ ok: false, code: TRANSPORT_ERROR });
  });

  it('discards the bound draft through its session', async () => {
    const { calls, execute } = recorder({ snapshot: null, problem: null, discarded: true });
    const s = createDraftSession({ executor: execute, machine: checkout, id: 'draft-1' });

    await expect(s.discard('d-1')).resolves.toEqual({ ok: true, snapshot: null });
    expect(calls[0]).toEqual({
      operation: 'discardSnapshot',
      input: { machine: checkout.id, id: 'draft-1', requestId: 'd-1' },
    });
  });
});
