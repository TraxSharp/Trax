import { SnapshotMachine } from '../../machine';
import type { Snapshot } from '../../types';
import type { FieldSchema, JsonFieldType, RuleSource } from '../interpreter';
import { machineFromIr, type IrDocument } from '../irMachine';

// Test code: a hand-written IR whose triggers declare input schemas, for the trigger-input validation tests.
// `Set` takes a range (`from` and `to` integers, an optional `note`, and `tags`, a list of at least one
// string); `Schedule` takes a nested record and a list of records; `Ping` declares no input at all. Written in
// the form the C# exporter produces: `integer` fields with their CLR range, `fields` on an object or array field
// for a nested record, and an input's constraints reading the input.

const integer: JsonFieldType = 'integer';

// An int field, as the C# exporter writes it: its CLR range.
const int32 = { minimum: -2147483648, maximum: 2147483647 };

const field = (
  name: string,
  type: JsonFieldType,
  extra: Partial<FieldSchema> = {},
): FieldSchema => ({ name, type, nullable: false, constraints: [], ...extra });

const tagsOf = (source: RuleSource) =>
  field('tags', 'array', {
    values: { type: 'string', nullable: false },
    constraints: [
      { rule: 'arrayOf', source, field: 'tags', type: 'string' },
      { rule: 'nonEmpty', source, field: 'tags' },
    ],
  });
const tags = tagsOf('context');

export const rangeIr = {
  id: 'range',
  version: 1,
  initialState: 'Idle',
  // The context holds a non-empty `tags`, so a constraint read against the context rather than the input
  // passes where it should refuse.
  initialContext: { tags: ['seed'] },
  states: ['Idle', 'Ranged'],
  triggers: ['Ping', 'Schedule', 'Set'],
  committedStates: [],
  context: {
    Idle: { fields: [tags] },
    Ranged: {
      fields: [
        field('from', 'number'),
        field('note', 'string', { nullable: true }),
        tags,
        field('to', 'number'),
      ],
    },
  },
  inputs: {
    Set: {
      fields: [
        field('from', integer, int32),
        field('note', 'string', { nullable: true }),
        tagsOf('input'),
        field('to', integer, int32),
      ],
    },
    Schedule: {
      fields: [
        field('slots', 'array', { fields: [field('at', integer, int32)] }),
        field('window', 'object', {
          fields: [field('end', integer, int32), field('start', integer, int32)],
        }),
      ],
    },
  },
  transitions: [
    { from: 'Idle', trigger: 'Ping', to: 'Idle' },
    { from: 'Idle', trigger: 'Schedule', to: 'Idle' },
    {
      from: 'Idle',
      trigger: 'Set',
      to: 'Ranged',
      reduce: {
        reduce: 'set',
        steps: [
          { field: 'from', value: { input: 'from' } },
          { field: 'note', value: { input: 'note' } },
          { field: 'tags', value: { input: 'tags' } },
          { field: 'to', value: { input: 'to' } },
        ],
      },
    },
  ],
} satisfies IrDocument;

export const rangeCore = new SnapshotMachine(machineFromIr(rangeIr));

export const idle: Snapshot = { machine: 'range', version: 1, state: 'Idle', context: { tags: ['seed'] } };

/** A `Set` input that passes every rule: the baseline each bad input changes one thing in. */
export const validRange = (): Record<string, unknown> => ({ from: 1, to: 2, tags: ['a'] });
