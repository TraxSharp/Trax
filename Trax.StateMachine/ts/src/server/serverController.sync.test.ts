import fs from 'node:fs';
import { describe, expect, it } from 'vitest';
import { conformanceFile } from '../fixtures';
import type { IrDocument } from '../rules/irMachine';
import { typedMachineFromIr } from '../typed';
import { ServerMachineController } from './serverController';
import { FakeMachineServer, flushMicrotasks, sequentialRequestIds, serverSnapshot } from './testing/FakeMachineServer';

// syncTo: a page that ran ahead of the server is brought back to it one planned event at a time, over the planner's
// wizard machine (conformance/plan/). Start -Begin-> Details, Update writes the fields, Next needs them, Back returns,
// Skip rests on a custom guard no handler is bound for, and Send runs an effect.

type Fields = { email: string | null; name: string | null };
interface WizardSpec {
  states: { Start: Fields; Details: Fields; Review: Fields; Sent: Fields };
  triggers: { Begin: undefined; Next: undefined; Back: undefined; Skip: undefined; Send: undefined; Update: Fields };
}

const wizard = typedMachineFromIr<WizardSpec>(
  JSON.parse(fs.readFileSync(conformanceFile('plan', 'wizard.ir.json'), 'utf8')) as IrDocument,
);
const ada = { email: 'ada@example.com', name: 'Ada' };
const toReview = { fields: { trigger: 'Update' as const, input: ada }, navigation: ['Begin', 'Next', 'Back'] as const };

async function ready(state: keyof WizardSpec['states'], context: Partial<Fields> = {}) {
  const server = new FakeMachineServer();
  const controller = new ServerMachineController<WizardSpec>({
    machine: wizard,
    server,
    id: 'i-1',
    newRequestId: sequentialRequestIds(),
    retryDelays: [],
  });
  const started = controller.start();
  await flushMicrotasks();
  server.gets[0].resolve({ kind: 'ok', snapshot: serverSnapshot({ state, context }) });
  await started;
  await flushMicrotasks();
  return { server, controller };
}

describe('ServerMachineController.syncTo', () => {
  it('sends the plan one event at a time, each planned from the answer before it', async () => {
    const { server, controller } = await ready('Start');

    const synced = controller.syncTo('Review', toReview);
    await flushMicrotasks();
    expect(controller.get().unsaved).toBe(true);
    expect(server.events.map((e) => e.request.trigger)).toEqual(['Begin']);

    server.events[0].resolve({ kind: 'ok', snapshot: serverSnapshot({ revision: 2, state: 'Details' }) });
    await flushMicrotasks();
    expect(server.events[1].request).toMatchObject({ trigger: 'Update', input: ada });

    server.events[1].resolve({ kind: 'ok', snapshot: serverSnapshot({ revision: 3, state: 'Details', context: ada }) });
    await flushMicrotasks();
    expect(server.events[2].request.trigger).toBe('Next');
    expect(server.events[2].request.clientResult).toBeDefined();

    server.events[2].resolve({ kind: 'ok', snapshot: serverSnapshot({ revision: 4, state: 'Review', context: ada }) });
    expect(await synced).toMatchObject({ outcome: 'synced', snapshot: { state: 'Review', revision: 4 } });
    expect(controller.get().unsaved).toBe(false);
    expect(server.events).toHaveLength(3);
  });

  it('sends nothing when the server already holds the target and the fields', async () => {
    const { server, controller } = await ready('Review', ada);

    expect(await controller.syncTo('Review', toReview)).toMatchObject({
      outcome: 'synced',
      snapshot: { state: 'Review', revision: 1, context: ada },
    });
    expect(server.events).toHaveLength(0);
    expect(controller.get().unsaved).toBe(false);
  });

  it('reads the instance once on a refusal and plans again from what it holds', async () => {
    const { server, controller } = await ready('Details', ada);

    const synced = controller.syncTo('Review', toReview);
    await flushMicrotasks();
    expect(server.events[0].request.trigger).toBe('Next');
    server.events[0].resolve({ kind: 'problem', code: 'GUARD_FAILED', message: 'No.' });
    await flushMicrotasks();

    // The fields were cleared meanwhile: the plan sends them again before Next.
    expect(server.gets).toHaveLength(2);
    server.gets[1].resolve({ kind: 'ok', snapshot: serverSnapshot({ revision: 2, state: 'Details' }) });
    await flushMicrotasks();
    expect(server.events[1].request.trigger).toBe('Update');
    server.events[1].resolve({ kind: 'ok', snapshot: serverSnapshot({ revision: 3, state: 'Details', context: ada }) });
    await flushMicrotasks();
    server.events[2].resolve({ kind: 'ok', snapshot: serverSnapshot({ revision: 4, state: 'Review', context: ada }) });

    expect(await synced).toMatchObject({ outcome: 'synced', snapshot: { revision: 4 } });
  });

  it('settles refused on a second refusal, and the view stays unsaved', async () => {
    const { server, controller } = await ready('Details', ada);

    const synced = controller.syncTo('Review', toReview);
    await flushMicrotasks();
    server.events[0].resolve({ kind: 'problem', code: 'GUARD_FAILED', message: 'No.' });
    await flushMicrotasks();
    server.gets[1].resolve({ kind: 'ok', snapshot: serverSnapshot({ revision: 2, state: 'Details', context: ada }) });
    await flushMicrotasks();
    server.events[1].resolve({ kind: 'problem', code: 'GUARD_FAILED', message: 'No.' });

    expect(await synced).toMatchObject({ outcome: 'refused', problem: { code: 'GUARD_FAILED' } });
    expect(server.gets).toHaveLength(2);
    expect(controller.get().unsaved).toBe(true);
  });

  it('settles refused at once on a refusal a re-read would not change', async () => {
    const { server, controller } = await ready('Details', ada);

    const synced = controller.syncTo('Review', toReview);
    await flushMicrotasks();
    server.events[0].resolve({ kind: 'problem', code: 'FORBIDDEN', message: 'No.' });

    expect(await synced).toMatchObject({ outcome: 'refused', problem: { code: 'FORBIDDEN' } });
    expect(server.gets).toHaveLength(1);
  });

  it('a later sync supersedes an earlier one once its event in flight answers', async () => {
    const { server, controller } = await ready('Start');

    const first = controller.syncTo('Review', toReview);
    await flushMicrotasks();
    const bob = { email: 'bob@example.com', name: 'Bob' };
    const second = controller.syncTo('Details', { ...toReview, fields: { trigger: 'Update', input: bob } });
    await flushMicrotasks();
    expect(server.events).toHaveLength(1);

    server.events[0].resolve({ kind: 'ok', snapshot: serverSnapshot({ revision: 2, state: 'Details' }) });
    expect(await first).toEqual({ outcome: 'superseded' });
    await flushMicrotasks();
    expect(server.events[1].request).toMatchObject({ trigger: 'Update', input: bob });
    server.events[1].resolve({ kind: 'ok', snapshot: serverSnapshot({ revision: 3, state: 'Details', context: bob }) });

    expect(await second).toMatchObject({ outcome: 'synced', snapshot: { state: 'Details', revision: 3 } });
    expect(server.events).toHaveLength(2);
    expect(controller.get().unsaved).toBe(false);
  });

  it('sends an edge on a custom guard without a prediction, then plans from the answer', async () => {
    const { server, controller } = await ready('Details');

    const synced = controller.syncTo('Review', { fields: toReview.fields, navigation: ['Skip'] });
    await flushMicrotasks();
    expect(server.events[0].request.trigger).toBe('Skip');
    expect(server.events[0].request.clientResult).toBeUndefined();
    server.events[0].resolve({ kind: 'ok', snapshot: serverSnapshot({ revision: 2, state: 'Review' }) });
    await flushMicrotasks();
    expect(server.events[1].request).toMatchObject({ trigger: 'Update', input: ada });
    server.events[1].resolve({ kind: 'ok', snapshot: serverSnapshot({ revision: 3, state: 'Review', context: ada }) });

    expect(await synced).toMatchObject({ outcome: 'synced', snapshot: { revision: 3 } });
  });

  it('waits for an event sent before it, and plans from its answer', async () => {
    const { server, controller } = await ready('Start');

    const sent = controller.send('Begin');
    const synced = controller.syncTo('Review', toReview);
    await flushMicrotasks();
    expect(server.events).toHaveLength(1);
    server.events[0].resolve({ kind: 'ok', snapshot: serverSnapshot({ revision: 2, state: 'Details' }) });
    expect(await sent).toMatchObject({ outcome: 'confirmed' });
    await flushMicrotasks();

    expect(server.events[1].request.trigger).toBe('Update');
    server.events[1].resolve({ kind: 'ok', snapshot: serverSnapshot({ revision: 3, state: 'Details', context: ada }) });
    await flushMicrotasks();
    server.events[2].resolve({ kind: 'ok', snapshot: serverSnapshot({ revision: 4, state: 'Review', context: ada }) });
    expect(await synced).toMatchObject({ outcome: 'synced' });
  });

  it('stops after twice the machine\'s states in events', async () => {
    const { server, controller } = await ready('Details');

    // A server that never keeps the fields: every plan starts with Update again.
    const synced = controller.syncTo('Review', toReview);
    for (let revision = 2; revision < 20 && server.events.length < 9; revision++) {
      await flushMicrotasks();
      const call = server.events.at(-1)!;
      if (!call.settled) call.resolve({ kind: 'ok', snapshot: serverSnapshot({ revision, state: 'Details' }) });
    }

    expect(await synced).toEqual({ outcome: 'exceeded' });
    expect(server.events).toHaveLength(2 * wizard.states.length);
  });

  it('answers the planner\'s unreachable and invalid without sending', async () => {
    const { server, controller } = await ready('Review', ada);

    expect(await controller.syncTo('Sent', { fields: null, navigation: ['Begin', 'Next', 'Back'] })).toEqual({
      outcome: 'unreachable',
    });
    expect(await controller.syncTo('Sent', { fields: null, navigation: ['Send'] })).toEqual({
      outcome: 'invalid',
      trigger: 'Send',
    });
    expect(server.events).toHaveLength(0);
    expect(controller.get().unsaved).toBe(true);
  });

  it('settles failed on a transport failure, and a second sync resumes from what the server confirmed', async () => {
    const { server, controller } = await ready('Details');

    const first = controller.syncTo('Review', toReview);
    await flushMicrotasks();
    server.events[0].fail();
    expect(await first).toMatchObject({ outcome: 'failed', problem: { code: 'TRANSPORT_ERROR' } });
    expect(controller.get().unsaved).toBe(true);

    const again = controller.syncTo('Review', toReview);
    await flushMicrotasks();
    expect(server.events[1].request.trigger).toBe('Update');
    server.events[1].resolve({ kind: 'ok', snapshot: serverSnapshot({ revision: 2, state: 'Details', context: ada }) });
    await flushMicrotasks();
    server.events[2].resolve({ kind: 'ok', snapshot: serverSnapshot({ revision: 3, state: 'Review', context: ada }) });
    expect(await again).toMatchObject({ outcome: 'synced' });
    expect(controller.get().unsaved).toBe(false);
  });

  it('fails NOT_READY before the first snapshot, and settles stopped once stopped', async () => {
    const server = new FakeMachineServer();
    const controller = new ServerMachineController<WizardSpec>({ machine: wizard, server, id: 'i-1' });
    expect(await controller.syncTo('Review', toReview)).toMatchObject({
      outcome: 'failed',
      problem: { code: 'NOT_READY' },
    });

    const started = controller.start();
    await flushMicrotasks();
    server.gets[0].resolve({ kind: 'problem', code: 'NOT_FOUND', message: 'gone' });
    await started;
    expect(await controller.syncTo('Review', toReview)).toMatchObject({ outcome: 'stopped', error: { code: 'GONE' } });
  });
});
