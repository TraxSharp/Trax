import { createServer, type Server } from 'node:http';
import type { AddressInfo } from 'node:net';

import { afterEach, describe, expect, it } from 'vitest';

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

const servers: Server[] = [];

// A loopback host that answers every request with `answer` and counts what it received.
const host = async (answer: (port: number) => { status: number; headers?: Record<string, string>; body?: string }) => {
  let received = 0;
  const server = createServer((request, response) => {
    received++;
    request.resume();
    request.on('end', () => {
      const { status, headers, body } = answer((server.address() as AddressInfo).port);
      response.writeHead(status, { 'Content-Type': 'application/json', ...headers });
      response.end(body ?? '');
    });
  });
  servers.push(server);
  await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve));
  const port = (server.address() as AddressInfo).port;
  return { url: `http://127.0.0.1:${port}/graphql`, received: () => received };
};

afterEach(async () => {
  await Promise.all(servers.splice(0).map((s) => new Promise((resolve) => s.close(resolve))));
});

describe('createMachineExecutor and a redirect', () => {
  it('fails a request the endpoint redirects, and the other host receives nothing', async () => {
    const other = await host(() => ({
      status: 200,
      body: JSON.stringify({ data: { machines: { checkout: { get: null } } } }),
    }));
    const endpoint = await host(() => ({ status: 307, headers: { Location: other.url } }));
    const session = createDraftSession({
      executor: createMachineExecutor(endpoint.url, {
        headers: () => ({ 'X-Api-Key': 'key' }),
        machines: [{ machine: checkout, documents: [get] }],
      }),
      machine: checkout,
      id: 'draft-1',
    });

    const loaded = await session.load();

    expect(loaded).toMatchObject({ ok: false, code: TRANSPORT_ERROR });
    expect(endpoint.received()).toBe(1);
    expect(other.received()).toBe(0);
  });

  it('fails an answer a fetch reached by following a redirect itself', async () => {
    const fetch = (async () => {
      const response = new Response(JSON.stringify({ data: { machines: { checkout: { get: null } } } }));
      Object.defineProperty(response, 'redirected', { value: true });
      return response;
    }) as typeof globalThis.fetch;
    const session = createDraftSession({
      executor: createMachineExecutor('/graphql', { fetch, machines: [{ machine: checkout, documents: [get] }] }),
      machine: checkout,
      id: 'draft-1',
    });

    expect(await session.load()).toMatchObject({ ok: false, code: TRANSPORT_ERROR });
  });
});
