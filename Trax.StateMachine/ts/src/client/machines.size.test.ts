import { describe, expect, it } from 'vitest';

import { checkout } from '../machines/checkout/checkout';
import { createDraftSession, TRANSPORT_ERROR } from './draftSession';
import { createMachineExecutor } from './machines';

const get = {
  id: `trax_machine_checkout_get_${checkout.schemaHash}0123456789abcdef`,
  machine: 'checkout',
  operation: 'get',
  irHash: checkout.schemaHash,
  document: 'query CheckoutGet($id: UUID!) { machines { checkout { get(id: $id) { id } } } }',
};

// An answer of `size` bytes or more: a not-found, padded with whitespace JSON ignores.
const padded = (size: number) => {
  const body = JSON.stringify({ data: { machines: { checkout: { get: null } } } });
  return body + ' '.repeat(Math.max(0, size - body.length));
};

// A fetch that answers `body` as a stream, saying its length or not, and records how much of it was read.
const answering = (body: string, sayLength: boolean) => {
  const bytes = new TextEncoder().encode(body);
  const read = { bytes: 0 };
  const fetch = (async () => {
    let offset = 0;
    const stream = new ReadableStream<Uint8Array>({
      pull(controller) {
        if (offset >= bytes.length) return controller.close();
        const chunk = bytes.subarray(offset, offset + 256);
        offset += chunk.length;
        read.bytes = offset;
        controller.enqueue(chunk);
      },
    });
    return new Response(stream, {
      status: 200,
      headers: sayLength ? { 'Content-Length': String(bytes.length) } : {},
    });
  }) as typeof globalThis.fetch;
  return { fetch, read };
};

const session = (fetch: typeof globalThis.fetch, maxResponseSize?: number) =>
  createDraftSession({
    executor: createMachineExecutor('/graphql', {
      fetch,
      maxResponseSize,
      machines: [{ machine: checkout, documents: [get] }],
    }),
    machine: checkout,
    id: 'draft-1',
  });

describe('createMachineExecutor and the size of an answer', () => {
  it.each([true, false])('fails an answer larger than the limit without reading it whole (length said: %s)', async (sayLength) => {
    const { fetch, read } = answering(padded(64 * 1024), sayLength);

    const loaded = await session(fetch, 1024).load();

    expect(loaded).toMatchObject({ ok: false, code: TRANSPORT_ERROR });
    expect(read.bytes).toBeLessThan(4096);
  });

  it('reads an answer within the limit', async () => {
    const { fetch } = answering(padded(512), false);

    expect(await session(fetch, 1024).load()).toEqual({ ok: true, snapshot: null });
  });

  it('limits an answer to 16 MiB by default', async () => {
    const { fetch } = answering(padded(16 * 1024 * 1024 + 1), false);

    expect(await session(fetch).load()).toMatchObject({ ok: false, code: TRANSPORT_ERROR });
  });
});
