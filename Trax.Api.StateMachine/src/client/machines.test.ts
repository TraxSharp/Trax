import { describe, expect, it } from 'vitest';

import { checkout } from '../machines/checkout/checkout';
import { createDraftSession, startDraft } from './draftSession';
import { createMachineExecutor, eventField, graphqlLiteral, machineField } from './machines';
import { createSnapshotClient } from './transport';

// A fetch that records each document and answers with the next canned body.
const server = (...bodies: unknown[]) => {
  const queries: string[] = [];
  const fetch = (async (_url: string, init: { body: string }) => {
    queries.push((JSON.parse(init.body) as { query: string }).query);
    return { json: async () => bodies.shift() };
  }) as unknown as typeof globalThis.fetch;
  return { queries, fetch };
};

const answer = (field: string, value: unknown) => ({ data: { machines: { checkout: { [field]: value } } } });

const executor = (fetch: typeof globalThis.fetch) =>
  createMachineExecutor('/graphql', {
    fetch,
    machines: [{ machine: checkout, context: 'items receipt total', effectEvent: 'Pay' }],
  });

describe('names', () => {
  it('names a machine field and an event field as the server does', () => {
    expect(machineField('topic-map')).toBe('topicMap');
    expect(machineField('map-2d')).toBe('map2d');
    expect(eventField('CancelBuild')).toBe('cancelBuild');
    expect(eventField('ID')).toBe('id');
    expect(eventField('URLPath')).toBe('urlPath');
  });

  it('writes a JSON value as a GraphQL literal with bare keys', () => {
    expect(graphqlLiteral({ title: 'A "b"', pages: 3, tags: ['x'], note: null })).toBe(
      '{ title: "A \\"b\\"", pages: 3, tags: ["x"], note: null }',
    );
  });
});

describe('createMachineExecutor', () => {
  it('starts through machines.<machine>.start and rebuilds the snapshot without null fields', async () => {
    const { queries, fetch } = server(
      answer('start', {
        id: 'draft-1',
        revision: 1,
        state: 'Cart',
        context: { items: [], receipt: null, total: 0 },
        problem: null,
      }),
    );

    const started = await startDraft({
      client: createSnapshotClient(executor(fetch)),
      machine: checkout,
      requestId: 'req-1',
    });

    expect(queries[0]).toBe(
      'mutation { machines { checkout { start(requestId: "req-1") { id revision state dueAt dueAtKnown context { items receipt total } problem { code message } } } } }',
    );
    expect(started).toEqual({
      ok: true,
      id: 'draft-1',
      created: true,
      revision: 1,
      snapshot: JSON.stringify({
        machine: 'checkout',
        version: checkout.version,
        state: 'Cart',
        context: { items: [], total: 0 },
      }),
    });
  });

  it('sends an event as its own field, with its input as a literal and the client result', async () => {
    const { queries, fetch } = server(
      answer('addItem', { id: 'draft-1', revision: 2, state: 'Cart', context: { items: ['book'] }, problem: null }),
    );
    const session = createDraftSession({
      client: createSnapshotClient(executor(fetch)),
      machine: checkout,
      id: 'draft-1',
    });

    const result = await session.advance('AddItem', {
      input: '{"item":"book"}',
      requestId: 'req-2',
      clientResult: '{"state":"Cart"}',
    });

    expect(queries[0]).toContain(
      'addItem(id: "draft-1", input: { item: "book" }, requestId: "req-2", clientResult: "{\\"state\\":\\"Cart\\"}")',
    );
    expect(result.ok).toBe(true);
  });

  it('sends the effect through its bound event, with no input', async () => {
    const { queries, fetch } = server(
      answer('pay', { id: 'draft-1', revision: 3, state: 'Paid', context: { receipt: 'r-1' }, problem: null }),
    );

    await createSnapshotClient(executor(fetch)).send('checkout', 'draft-1', 'pay-1');

    expect(queries[0]).toContain('pay(id: "draft-1", requestId: "pay-1")');
  });

  it('reads with get, and answers a missing instance as not-found, which a session reads as no draft', async () => {
    const { queries, fetch } = server({ data: { machines: { checkout: { get: null } } } });
    const session = createDraftSession({
      client: createSnapshotClient(executor(fetch)),
      machine: checkout,
      id: 'draft-1',
    });

    expect(await session.load()).toEqual({ ok: true, snapshot: null });
    expect(queries[0]).toMatch(/^\{ machines \{ checkout \{ get\(id: "draft-1"\)/);
  });

  it('answers a refusal as the problem, and refuses a save without asking the server', async () => {
    const { queries, fetch } = server(
      answer('next', { id: 'draft-1', revision: null, state: null, context: null, problem: { code: 'guard-failed', message: 'Add an item.' } }),
    );
    const client = createSnapshotClient(executor(fetch));

    expect((await client.advance('checkout', 'draft-1', 'Next')).problem).toEqual({
      code: 'guard-failed',
      message: 'Add an item.',
    });
    expect((await client.save('checkout', 'draft-1', '{}')).problem?.code).toBe('save-not-allowed');
    expect(queries).toHaveLength(1);
  });

  it('discards through machines.<machine>.discard', async () => {
    const { queries, fetch } = server(answer('discard', { id: 'draft-1', discarded: true, problem: null }));

    const discarded = await createSnapshotClient(executor(fetch)).discard('checkout', 'draft-1', 'req-3');

    expect(discarded.discarded).toBe(true);
    expect(queries[0]).toContain('discard(id: "draft-1", requestId: "req-3") { id discarded problem { code message } }');
  });

  it('throws on a GraphQL error, which a session reports as a transport error', async () => {
    const { fetch } = server({ errors: [{ message: 'The current user is not authorized.' }] });
    const session = createDraftSession({
      client: createSnapshotClient(executor(fetch)),
      machine: checkout,
      id: 'draft-1',
    });

    expect((await session.advance('Next')).ok).toBe(false);
  });
});

describe('createMachineExecutor with persisted documents', () => {
  // A fetch that records each request body and answers with the next canned body.
  const recording = (...bodies: unknown[]) => {
    const sent: Record<string, unknown>[] = [];
    const fetch = (async (_url: string, init: { body: string }) => {
      sent.push(JSON.parse(init.body) as Record<string, unknown>);
      return { json: async () => bodies.shift() };
    }) as unknown as typeof globalThis.fetch;
    return { sent, fetch };
  };

  const document = (operation: string, text: string, irHash = checkout.schemaHash) => ({
    id: `trax_machine_checkout_${operation}_${irHash}0123456789abcdef`,
    machine: 'checkout',
    operation,
    irHash,
    document: text,
  });

  const documents = [
    document('start', 'mutation CheckoutStart($requestId: String!) { machines { checkout { start(requestId: $requestId) { id } } } }'),
    document(
      'addItem',
      'mutation CheckoutAddItem($id: UUID!, $input: CheckoutAddItemInput!, $requestId: String!, $clientResult: String) { x }',
    ),
    document('get', 'query CheckoutGet($id: UUID!) { machines { checkout { get(id: $id) { id } } } }'),
  ];

  const persisted = (fetch: typeof globalThis.fetch, given = documents) =>
    createMachineExecutor('/graphql', { fetch, machines: [{ machine: checkout, documents: given }] });

  it('sends each operation by its id, with only the variables its document declares, and no document', async () => {
    const { sent, fetch } = recording(
      answer('start', { id: 'draft-1', revision: 1, state: 'Cart', context: { items: [] }, problem: null }),
      answer('addItem', { id: 'draft-1', revision: 2, state: 'Cart', context: { items: ['book'] }, problem: null }),
    );
    const client = createSnapshotClient(persisted(fetch));

    await client.start('checkout', 'req-1');
    await client.advance('checkout', 'draft-1', 'AddItem', {
      input: '{"item":"book"}',
      requestId: 'req-2',
      clientResult: '{"state":"Cart"}',
    });

    expect(sent[0]).toEqual({ id: documents[0].id, variables: { requestId: 'req-1' } });
    expect(sent[1]).toEqual({
      id: documents[1].id,
      variables: { id: 'draft-1', input: { item: 'book' }, requestId: 'req-2', clientResult: '{"state":"Cart"}' },
    });
    expect(sent.every((body) => !('query' in body))).toBe(true);
  });

  it('answers client-outdated and server-outdated as problems, which a session reports with their codes', async () => {
    const { fetch } = recording(
      { errors: [{ message: 'Reload.', extensions: { code: 'client-outdated' } }] },
      { errors: [{ message: 'Retry.', extensions: { code: 'server-outdated' } }] },
    );
    const session = createDraftSession({
      client: createSnapshotClient(persisted(fetch)),
      machine: checkout,
      id: 'draft-1',
    });

    expect(await session.load()).toEqual({ ok: false, code: 'client-outdated', message: 'Reload.' });
    expect(await session.load()).toEqual({ ok: false, code: 'server-outdated', message: 'Retry.' });
  });

  it('answers an operation it has no document for as client-outdated, without asking the server', async () => {
    const { sent, fetch } = recording();

    const discarded = await createSnapshotClient(persisted(fetch)).discard('checkout', 'draft-1', 'req-3');

    expect(discarded.problem?.code).toBe('client-outdated');
    expect(sent).toHaveLength(0);
  });

  it('refuses documents of another machine, or of another IR than the twin', () => {
    const { fetch } = recording();
    expect(() => persisted(fetch, [{ ...documents[0], machine: 'other' }])).toThrow(/machine 'other'/);
    expect(() => persisted(fetch, [document('start', 'x', 'f'.repeat(64))])).toThrow(/another version/);
  });
});
