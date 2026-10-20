import { SnapshotMachine } from '../../machine';
import { machineFromIr, type IrDocument } from '../../rules/irMachine';
import type { Snapshot } from '../../types';

// Test machines for timers, hand-written as IR (nothing is generated from them).
//
// `reminder`: Idle -(Open)-> Review; Review -(Approve)-> Done; Review -(Extend)-> Review; and Review's timer
// moves it to Expired after a fixed ten minutes.
//
// `wait`: Waiting -(Finish)-> Done; Waiting's timer moves it to TimedOut after the duration in the context's
// `patience` field, bounded by one minute and one hour.

export const reminderIr: IrDocument = {
  id: 'reminder',
  version: 1,
  initialState: 'Idle',
  initialContext: {},
  states: ['Done', 'Expired', 'Idle', 'Review'],
  triggers: ['Approve', 'Extend', 'Open'],
  committedStates: [],
  context: {
    Done: { fields: [] },
    Expired: { fields: [] },
    Idle: { fields: [] },
    Review: { fields: [] },
  },
  inputs: {},
  transitions: [
    { from: 'Idle', trigger: 'Open', to: 'Review' },
    { from: 'Review', trigger: 'Approve', to: 'Done' },
    { from: 'Review', trigger: 'Extend', to: 'Review' },
  ],
  timers: [{ state: 'Review', trigger: 'Review.after', target: 'Expired', after: 'PT10M' }],
};

export const reminderCore = new SnapshotMachine(machineFromIr(reminderIr));

const patience = {
  fields: [
    {
      name: 'patience',
      type: 'string' as const,
      nullable: false,
      constraints: [],
    },
  ],
};

export const waitIr: IrDocument = {
  id: 'wait',
  version: 1,
  initialState: 'Waiting',
  initialContext: { patience: 'PT5M' },
  states: ['Done', 'TimedOut', 'Waiting'],
  triggers: ['Finish'],
  committedStates: [],
  context: { Done: patience, TimedOut: patience, Waiting: patience },
  inputs: {},
  transitions: [{ from: 'Waiting', trigger: 'Finish', to: 'Done' }],
  timers: [
    {
      state: 'Waiting',
      trigger: 'Waiting.after',
      target: 'TimedOut',
      field: { name: 'patience', min: 'PT1M', max: 'PT1H' },
    },
  ],
};

export const waitCore = new SnapshotMachine(machineFromIr(waitIr));

export const reminderAt = (state: string): Snapshot => ({ machine: 'reminder', version: 1, state, context: {} });
