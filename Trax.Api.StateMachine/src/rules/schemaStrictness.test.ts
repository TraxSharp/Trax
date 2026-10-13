import { describe, expect, it } from 'vitest';
import { RejectionReasons } from '../results';
import { validateInput, validateSchema, type ContextSchema, type FieldSchema } from './interpreter';
import { idle, rangeCore, validRange } from './testing/rangeMachine';

// The exported schema is as strict as the CLR type, in both engines: an integer field carries its CLR range, the
// values of a dictionary and the elements of a list are checked against their own type. The mirror of the C#
// SchemaStrictnessTests: the schemas below are what the C# exporter writes for its Widths and Maps records, and
// each case gets the verdict the C# engine gives.

const field = (name: string, extra: Partial<FieldSchema>): FieldSchema => ({
  name,
  type: 'integer',
  nullable: false,
  constraints: [],
  ...extra,
});

// Past 2^53 a bound is the nearest double inside the CLR range.
const ranges: Record<string, { minimum: number; maximum: number }> = {
  int: { minimum: -2147483648, maximum: 2147483647 },
  long: { minimum: -9223372036854774784, maximum: 9223372036854774784 },
  short: { minimum: -32768, maximum: 32767 },
  byte: { minimum: 0, maximum: 255 },
  sByte: { minimum: -128, maximum: 127 },
  uInt: { minimum: 0, maximum: 4294967295 },
  uLong: { minimum: 0, maximum: 18446744073709549568 },
  uShort: { minimum: 0, maximum: 65535 },
};

const widths: ContextSchema = {
  fields: [
    ...Object.entries(ranges).map(([name, range]) => field(name, range)),
    field('maybeInt', { nullable: true, ...ranges.int }),
  ],
};

// One field replaced by the number the JSON text reads as.
const widthsWith = (name: string, number: string): Record<string, unknown> => ({
  int: 0,
  long: 0,
  short: 0,
  byte: 0,
  sByte: 0,
  uInt: 0,
  uLong: 0,
  uShort: 0,
  maybeInt: null,
  [name]: JSON.parse(number) as unknown,
});

describe('an integer field is as strict as its CLR type', () => {
  it.each([
    ['int', '1e10'],
    ['int', '2147483648'],
    ['int', '-2147483649'],
    ['short', '32768'],
    ['byte', '256'],
    ['byte', '-1'],
    ['sByte', '128'],
    ['uInt', '-1'],
    ['uInt', '4294967296'],
    ['uShort', '65536'],
    ['maybeInt', '1e10'],
    // long.MaxValue + 1 and long.MaxValue are one double; neither engine can tell them apart, so both refuse.
    ['long', '9223372036854775808'],
    ['long', '9223372036854775807'],
    ['long', '-9223372036854775809'],
    ['long', '1e19'],
    ['uLong', '-1'],
    ['uLong', '18446744073709551616'],
  ])('refuses %s = %s', (name, number) => {
    expect(validateInput(widths, widthsWith(name, number))).not.toBeNull();
  });

  it.each([
    ['int', '2147483647'],
    ['int', '-2147483648'],
    ['short', '-32768'],
    ['byte', '255'],
    ['sByte', '-128'],
    ['uInt', '4294967295'],
    ['uShort', '65535'],
    ['long', '9223372036854774784'],
    ['long', '-9223372036854774784'],
    ['long', '9007199254740993'],
    ['uLong', '18446744073709549568'],
    ['int', '1e9'],
  ])('accepts %s = %s', (name, number) => {
    expect(validateInput(widths, widthsWith(name, number))).toBeNull();
  });

  it('refuses an out-of-range input as invalid-input through the engine', () => {
    expect(rangeCore.advance(idle, 'Set', { ...validRange(), from: 1e10 })).toMatchObject({
      outcome: 'rejected',
      reason: RejectionReasons.InvalidInput,
    });
  });
});

const maps: ContextSchema = {
  fields: [
    {
      name: 'anything',
      type: 'object',
      nullable: false,
      constraints: [],
    },
    {
      name: 'bytes',
      type: 'object',
      nullable: false,
      constraints: [],
      values: { type: 'array', nullable: false, values: { type: 'integer', nullable: false, ...ranges.byte } },
    },
    {
      name: 'counts',
      type: 'object',
      nullable: false,
      constraints: [],
      values: { type: 'integer', nullable: false, ...ranges.int },
    },
    {
      name: 'inners',
      type: 'object',
      nullable: false,
      constraints: [],
      values: { type: 'object', nullable: false, fields: [field('count', ranges.int)] },
    },
    {
      name: 'longs',
      type: 'array',
      nullable: false,
      constraints: [{ rule: 'arrayOf', source: 'input', field: 'longs', type: 'integer' }],
      values: { type: 'integer', nullable: false, ...ranges.long },
    },
    {
      name: 'notes',
      type: 'object',
      nullable: false,
      constraints: [],
      values: { type: 'string', nullable: true },
    },
  ],
};

const validMaps = (): Record<string, Record<string, unknown> | unknown[]> => ({
  counts: { a: 1 },
  notes: { a: 'x', b: null },
  inners: { a: { count: 1 } },
  bytes: { a: [1, 255] },
  longs: [1, 2],
  anything: { a: [], b: 'x' },
});

const mapsWith = (change: (input: ReturnType<typeof validMaps>) => void) => {
  const input = validMaps();
  change(input);
  return input;
};

describe('dictionary values and list elements are checked against their type', () => {
  it('accepts a valid map input', () => {
    expect(validateInput(maps, validMaps())).toBeNull();
  });

  it('refuses a dictionary value of the wrong type', () => {
    expect(validateInput(maps, mapsWith((i) => ((i.counts as Record<string, unknown>).a = 'one')))).not.toBeNull();
  });

  it('refuses a dictionary value out of its range', () => {
    expect(validateInput(maps, mapsWith((i) => ((i.counts as Record<string, unknown>).a = 1e10)))).not.toBeNull();
  });

  it('refuses a null dictionary value unless the value type is nullable', () => {
    expect(validateInput(maps, mapsWith((i) => ((i.counts as Record<string, unknown>).a = null)))).not.toBeNull();
    expect(validateInput(maps, mapsWith((i) => ((i.notes as Record<string, unknown>).a = null)))).toBeNull();
  });

  it('checks each record of a dictionary of records', () => {
    expect(validateInput(maps, mapsWith((i) => (i.inners = { a: { count: 'one' } })))).not.toBeNull();
    expect(validateInput(maps, mapsWith((i) => (i.inners = { a: { count: 1, extra: 1 } })))).not.toBeNull();
  });

  it('checks each element of a dictionary of lists', () => {
    expect(validateInput(maps, mapsWith((i) => (i.bytes = { a: [1, 256] })))).not.toBeNull();
  });

  it('refuses a list element out of its range', () => {
    expect(validateInput(maps, mapsWith((i) => (i.longs = [1, 1e19])))).not.toBeNull();
  });

  it('checks a dictionary value in a context too', () => {
    expect(validateSchema(maps, mapsWith((i) => ((i.counts as Record<string, unknown>).a = 'one')))).not.toBeNull();
  });

  it('reads an own __proto__ key as a dictionary value, not the prototype', () => {
    const input = validMaps();
    input.counts = JSON.parse('{"__proto__":"one"}') as Record<string, unknown>;
    expect(validateInput(maps, input)).not.toBeNull();
  });
});
