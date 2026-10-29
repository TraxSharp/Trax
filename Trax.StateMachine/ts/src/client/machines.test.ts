import { afterEach, describe, expect, it, vi } from 'vitest';

import { checkout } from '../machines/checkout/checkout';
import { createDraftSession, startDraft } from './draftSession';
import { createMachineExecutor, eventField, machineField } from './machines';
import { createSnapshotClient } from './transport';

const answer = (field: string, value: unknown) => ({ data: { machines: { checkout: { [field]: value } } } });

// A fetch that records each request body and answers with the next canned body.
const recording = (...bodies: unknown[]) => {
  const sent: Record<string, unknown>[] = [];
  const fetch = (async (_url: string, init: { body: string }) => {
    sent.push(JSON.parse(init.body) as Record<string, unknown>);
    return new Response(JSON.stringify(bodies.shift()), { status: 200, statusText: 'OK' });
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
  document('next', 'mutation CheckoutNext($id: UUID!, $requestId: String!, $clientResult: String) { x }'),
  document('pay', 'mutation CheckoutPay($id: UUID!, $requestId: String!) { x }'),
  document('get', 'query CheckoutGet($id: UUID!) { machines { checkout { get(id: $id) { id } } } }'),
  document('discard', 'mutation CheckoutDiscard($id: UUID!, $requestId: String!) { x }'),
];

const persisted = (fetch: typeof globalThis.fetch, given = documents) =>
  createMachineExecutor('/graphql', { fetch, machines: [{ machine: checkout, documents: given, effectEvent: 'Pay' }] });

describe('names', () => {
  it('names a machine field and an event field as the server does', () => {
    expect(machineField('topic-map')).toBe('topicMap');
    expect(machineField('map-2d')).toBe('map2d');
    expect(eventField('CancelBuild')).toBe('cancelBuild');
    expect(eventField('ID')).toBe('id');
    expect(eventField('URLPath')).toBe('urlPath');
  });
});

describe('createMachineExecutor', () => {
  it('refuses a cleartext endpoint on a host that is not loopback, naming the host but not the query', () => {
    const { fetch } = recording();
    const build = (endpoint: string, allowInsecureTransport?: boolean) => () =>
      createMachineExecutor(endpoint, { fetch, allowInsecureTransport, machines: [{ machine: checkout, documents }] });

    expect(build('http://api.example.com/graphql?access_token=SECRET')).toThrow(/http:\/\/api\.example\.com is not over TLS/);
    expect(build('http://api.example.com/graphql?access_token=SECRET')).not.toThrow(/SECRET/);
    expect(build('http://10.0.0.5/graphql')).toThrow(/not over TLS/);
    expect(build('ws://api.example.com/graphql')).toThrow(/not over TLS/);
  });

  it('accepts TLS, a loopback host, a relative endpoint, or cleartext the caller allowed', () => {
    const { fetch } = recording();
    const build = (endpoint: string, allowInsecureTransport?: boolean) => () =>
      createMachineExecutor(endpoint, { fetch, allowInsecureTransport, machines: [{ machine: checkout, documents }] });

    for (const endpoint of [
      'https://api.example.com/graphql',
      'http://localhost:5000/graphql',
      'http://127.0.0.1:5000/graphql',
      'http://127.8.9.10/graphql',
      'http://[::1]:5000/graphql',
      '/graphql',
    ])
      expect(build(endpoint)).not.toThrow();
    expect(build('http://api.example.com/graphql', true)).not.toThrow();
  });

  it('refuses a scheme-relative endpoint, which names another host, unless the caller allowed it', () => {
    const { fetch } = recording();
    const build = (endpoint: string, allowInsecureTransport?: boolean) => () =>
      createMachineExecutor(endpoint, { fetch, allowInsecureTransport, machines: [{ machine: checkout, documents }] });

    for (const endpoint of [
      '//api.example.com/graphql',
      '\\\\api.example.com\\graphql',
      '/\\api.example.com/graphql',
      '\\/api.example.com/graphql',
      ' //api.example.com/graphql',
      '/\t/api.example.com/graphql',
    ]) {
      expect(build(endpoint)).toThrow(/another host/);
      expect(build(endpoint, true)).not.toThrow();
    }
  });

  it('refuses a machine with no documents, naming the manifest', () => {
    const { fetch } = recording();

    expect(() =>
      createMachineExecutor('/graphql', { fetch, machines: [{ machine: checkout, documents: [] }] }),
    ).toThrow(/no persisted documents.*client manifest/);
  });

  it('starts by its document and rebuilds the snapshot without null fields', async () => {
    const { fetch } = recording(
      answer('start', {
        id: 'draft-1',
        revision: 1,
        state: 'Cart',
        context: { items: [], receipt: null, total: 0 },
        problem: null,
      }),
    );

    const started = await startDraft({ executor: persisted(fetch), machine: checkout, requestId: 'req-1' });

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

  it('sends the effect through its bound event, with no input', async () => {
    const { sent, fetch } = recording(
      answer('pay', { id: 'draft-1', revision: 3, state: 'Paid', context: { receipt: 'r-1' }, problem: null }),
    );

    await createSnapshotClient(persisted(fetch)).send('checkout', 'draft-1', 'pay-1');

    expect(sent[0]).toEqual({ id: documents[3].id, variables: { id: 'draft-1', requestId: 'pay-1' } });
  });

  it('reads with get, and answers a missing instance as not-found, which a session reads as no draft', async () => {
    const { sent, fetch } = recording({ data: { machines: { checkout: { get: null } } } });
    const session = createDraftSession({ executor: persisted(fetch), machine: checkout, id: 'draft-1' });

    expect(await session.load()).toEqual({ ok: true, snapshot: null });
    expect(sent[0]).toEqual({ id: documents[4].id, variables: { id: 'draft-1' } });
  });

  it('answers a refusal as the problem, and refuses a save without asking the server', async () => {
    const { sent, fetch } = recording(
      answer('next', {
        id: 'draft-1',
        revision: null,
        state: null,
        context: null,
        problem: { code: 'GUARD_FAILED', message: 'Add an item.' },
      }),
    );
    const client = createSnapshotClient(persisted(fetch));

    expect((await client.advance('checkout', 'draft-1', 'Next')).problem).toEqual({
      code: 'GUARD_FAILED',
      message: 'Add an item.',
    });
    expect((await client.save('checkout', 'draft-1', '{}')).problem?.code).toBe('SAVE_NOT_ALLOWED');
    expect(sent).toHaveLength(1);
  });

  it('discards by its document', async () => {
    const { sent, fetch } = recording(answer('discard', { id: 'draft-1', discarded: true, problem: null }));

    const discarded = await createSnapshotClient(persisted(fetch)).discard('checkout', 'draft-1', 'req-3');

    expect(discarded.discarded).toBe(true);
    expect(sent[0]).toEqual({ id: documents[5].id, variables: { id: 'draft-1', requestId: 'req-3' } });
  });

  it('refuses a trigger the machine does not declare as an event, and never sends it as another operation', async () => {
    const { sent, fetch } = recording();
    const client = createSnapshotClient(persisted(fetch));

    for (const trigger of ['Start', 'Get', 'Discard', 'Changed', 'Nope'])
      expect((await client.advance('checkout', 'draft-1', trigger)).problem?.code).toBe('NO_TRANSITION');
    expect(sent).toEqual([]);
  });

  it('refuses an operation name as an event even without the machine definition, and an effect named like one', async () => {
    const { sent, fetch } = recording();
    const bare = createSnapshotClient(
      createMachineExecutor('/graphql', {
        fetch,
        machines: [{ machine: { id: 'checkout', version: 1 }, documents, effectEvent: 'Discard' }],
      }),
    );

    for (const trigger of ['Start', 'Get', 'Discard', 'Changed'])
      expect((await bare.advance('checkout', 'draft-1', trigger)).problem?.code).toBe('NO_TRANSITION');
    expect((await bare.send('checkout', 'draft-1')).problem?.code).toBe('NO_TRANSITION');
    expect(sent).toEqual([]);
  });

  it('throws on a GraphQL error, which a session reports as a transport error', async () => {
    const { fetch } = recording({ errors: [{ message: 'The current user is not authorized.' }] });
    const session = createDraftSession({ executor: persisted(fetch), machine: checkout, id: 'draft-1' });

    expect((await session.advance('Next')).ok).toBe(false);
  });
});

describe('createMachineExecutor with persisted documents', () => {
  it('sends each operation by its id, with only the variables its document declares, and no document', async () => {
    const { sent, fetch } = recording(
      answer('start', { id: 'draft-1', revision: 1, state: 'Cart', context: { items: [] }, problem: null }),
      answer('addItem', { id: 'draft-1', revision: 2, state: 'Cart', context: { items: ['book'] }, problem: null }),
    );
    // A machine given by its id alone, whose events the executor takes from the documents.
    const client = createSnapshotClient(
      createMachineExecutor('/graphql', { fetch, machines: [{ machine: { id: 'checkout', version: 1 }, documents }] }),
    );

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
      { errors: [{ message: 'Reload.', extensions: { code: 'CLIENT_OUTDATED' } }] },
      { errors: [{ message: 'Retry.', extensions: { code: 'SERVER_OUTDATED' } }] },
    );
    const session = createDraftSession({ executor: persisted(fetch), machine: checkout, id: 'draft-1' });

    expect(await session.load()).toEqual({ ok: false, code: 'CLIENT_OUTDATED', message: 'Reload.' });
    expect(await session.load()).toEqual({ ok: false, code: 'SERVER_OUTDATED', message: 'Retry.' });
  });

  it('throws on a status that is not 2xx, whatever answer its body carries', async () => {
    const body = answer('get', { id: 'draft-1', revision: 2, state: 'Cart', context: { items: [] }, problem: null });
    const fetch = (async () =>
      new Response(JSON.stringify(body), { status: 500, statusText: 'Internal Server Error' })) as typeof globalThis.fetch;
    const session = createDraftSession({ executor: persisted(fetch), machine: checkout, id: 'draft-1' });

    const loaded = await session.load();

    expect(loaded.ok).toBe(false);
  });

  it('answers client-outdated on any status, as the host may choose one', async () => {
    const fetch = (async () =>
      new Response(JSON.stringify({ errors: [{ message: 'Reload.', extensions: { code: 'CLIENT_OUTDATED' } }] }), {
        status: 409,
        statusText: 'Conflict',
      })) as typeof globalThis.fetch;
    const session = createDraftSession({ executor: persisted(fetch), machine: checkout, id: 'draft-1' });

    expect(await session.load()).toEqual({ ok: false, code: 'CLIENT_OUTDATED', message: 'Reload.' });
  });

  it('answers an operation it has no document for as client-outdated, without asking the server', async () => {
    const { sent, fetch } = recording();

    const discarded = await createSnapshotClient(persisted(fetch, documents.slice(0, 2))).discard(
      'checkout',
      'draft-1',
      'req-3',
    );

    expect(discarded.problem?.code).toBe('CLIENT_OUTDATED');
    expect(sent).toHaveLength(0);
  });

  it('refuses documents of another machine, or of another IR than the twin', () => {
    const { fetch } = recording();
    expect(() => persisted(fetch, [{ ...documents[0], machine: 'other' }])).toThrow(/machine 'other'/);
    expect(() => persisted(fetch, [document('start', 'x', 'f'.repeat(64))])).toThrow(/another version/);
  });
});

describe('createMachineExecutor and a host that does not answer', () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  const load = (fetch: typeof globalThis.fetch, requestTimeout?: number) =>
    createDraftSession({
      executor: createMachineExecutor('/graphql', { fetch, requestTimeout, machines: [{ machine: checkout, documents }] }),
      machine: checkout,
      id: 'draft-1',
    }).load();

  it('fails a request whose answer never comes after 30 seconds, and aborts it', async () => {
    vi.useFakeTimers();
    let signal: AbortSignal | undefined;
    const fetch = ((_url: string, init: RequestInit) => {
      signal = init.signal ?? undefined;
      return new Promise<Response>(() => {});
    }) as unknown as typeof globalThis.fetch;

    const loaded = load(fetch);
    await vi.advanceTimersByTimeAsync(29_999);
    expect(signal?.aborted).toBe(false);
    await vi.advanceTimersByTimeAsync(1);

    expect(await loaded).toMatchObject({ ok: false, code: 'TRANSPORT_ERROR' });
    expect(signal?.aborted).toBe(true);
  });

  it('fails a request whose body never ends within the timeout given', async () => {
    vi.useFakeTimers();
    const fetch = (async () =>
      new Response(new ReadableStream<Uint8Array>({ pull: () => new Promise(() => {}) }), {
        status: 200,
      })) as unknown as typeof globalThis.fetch;

    const loaded = load(fetch, 1000);
    await vi.advanceTimersByTimeAsync(1000);

    expect(await loaded).toMatchObject({ ok: false, code: 'TRANSPORT_ERROR' });
  });
});
