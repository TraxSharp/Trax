import { Readable } from 'node:stream';

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

  // A response as an XHR polyfill or React Native hands it back: no body stream, only `text()`, which reads it whole.
  const withoutStream = (body: string, headers: Record<string, string> = {}) => {
    const read = { whole: false };
    const fetch = (async () =>
      ({
        ok: true,
        status: 200,
        statusText: 'OK',
        redirected: false,
        type: 'basic',
        headers: new Headers(headers),
        body: null,
        text: async () => {
          read.whole = true;
          return body;
        },
      }) as unknown as Response) as typeof globalThis.fetch;
    return { fetch, read };
  };

  it('refuses an answer with no body stream and no length before reading it', async () => {
    const { fetch, read } = withoutStream(padded(32 * 1024 * 1024));

    expect(await session(fetch).load()).toMatchObject({ ok: false, code: TRANSPORT_ERROR });
    expect(read.whole).toBe(false);
  });

  it('reads an answer with no body stream when it says a length within the limit', async () => {
    const body = padded(512);
    const { fetch } = withoutStream(body, { 'Content-Length': String(body.length) });

    expect(await session(fetch, 1024).load()).toEqual({ ok: true, snapshot: null });
  });

  // A fetch whose body is a Node stream, which has no `getReader`.
  const nodeStream = (body: string) => {
    const read = { bytes: 0 };
    const bytes = Buffer.from(body);
    const fetch = (async () =>
      ({
        ok: true,
        status: 200,
        statusText: 'OK',
        redirected: false,
        type: 'basic',
        headers: new Headers(),
        body: Readable.from(
          (function* () {
            for (let offset = 0; offset < bytes.length; offset += 256) {
              read.bytes = Math.min(bytes.length, offset + 256);
              yield bytes.subarray(offset, offset + 256);
            }
          })(),
        ),
        text: async () => body,
      }) as unknown as Response) as typeof globalThis.fetch;
    return { fetch, read };
  };

  it('reads a Node stream body', async () => {
    const { fetch } = nodeStream(padded(512));

    expect(await session(fetch, 1024).load()).toEqual({ ok: true, snapshot: null });
  });

  it('fails a Node stream body larger than the limit without reading it whole', async () => {
    const { fetch, read } = nodeStream(padded(64 * 1024));

    expect(await session(fetch, 1024).load()).toMatchObject({ ok: false, code: TRANSPORT_ERROR });
    expect(read.bytes).toBeLessThan(8192);
  });
});
