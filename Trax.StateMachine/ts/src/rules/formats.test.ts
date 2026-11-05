import { describe, expect, it } from 'vitest';
import { isInstant, isOneOf } from './formats';
import { validateInput, type ContextSchema } from './interpreter';

// A string field is as strict as its C# type: an enum's names, and the formats of a Guid, a date, a time, a
// duration, a URI and a char. Each format's verdicts are in conformance/strict-input/, written from the CLR and
// replayed by strictInput.conformance.test.ts; these are the checks that need a schema built by hand.

describe('formats', () => {
  it('compares enum names ignoring ASCII case only, and a renamed member exactly', () => {
    const names = ['red', 'darkGreen', 'sky-blue'];
    const exact = ['sky-blue'];
    expect(isOneOf('DarkGreen', names, exact)).toBe(true);
    expect(isOneOf('sky-blue', names, exact)).toBe(true);
    expect(isOneOf('SKY-BLUE', names, exact)).toBe(false);
    expect(isOneOf('purple', names, exact)).toBe(false);
    expect(isOneOf('skyBlue', names, exact)).toBe(false);
  });

  it('reads an instant only with its offset, a real date and a clock time', () => {
    for (const ok of ['2026-10-22T10:10:00Z', '2026-10-22T12:10:00.000+02:00', '2026-10-22T05:20:00.123456789-05:00'])
      expect(isInstant(ok)).toBe(true);
    for (const bad of [
      'not a time',
      '2026-10-22T10:10:00',
      '2026-10-22',
      '2026-02-30T10:10:00Z',
      '2026-10-22T24:00:00Z',
      '2026-10-22T10:10:00+15:00',
      '2026-10-22T10:10:00+02:60',
      '2026-10-22 10:10:00Z',
      ' 2026-10-22T10:10:00Z',
    ])
      expect(isInstant(bad)).toBe(false);
  });

  it('refuses a value outside an enum or format, in a field and in a list or map value', () => {
    const schema: ContextSchema = {
      fields: [
        { name: 'color', type: 'string', nullable: false, constraints: [], enum: ['red', 'darkGreen'] },
        { name: 'id', type: 'string', nullable: true, constraints: [], format: 'uuid' },
        {
          name: 'owners',
          type: 'object',
          nullable: true,
          constraints: [],
          values: { type: 'string', nullable: false, format: 'uuid' },
        },
        {
          name: 'amount',
          type: 'number',
          nullable: true,
          constraints: [],
          minimum: -79228162514264320000000000000,
          maximum: 79228162514264320000000000000,
        },
      ],
    };
    expect(validateInput(schema, { color: 'red' })).toBeNull();
    expect(validateInput(schema, { color: 'Purple' })).not.toBeNull();
    expect(validateInput(schema, { color: 'red', id: 'not-a-guid' })).not.toBeNull();
    expect(validateInput(schema, { color: 'red', owners: { a: 'nope' } })).not.toBeNull();
    expect(validateInput(schema, { color: 'red', amount: 1e30 })).not.toBeNull();
  });

  it("checks a map's keys as its C# key type, an integer's exactly", () => {
    const schema: ContextSchema = {
      fields: [
        {
          name: 'byNumber',
          type: 'object',
          nullable: false,
          constraints: [],
          values: { type: 'string', nullable: false },
          keys: { type: 'integer', nullable: false, minimum: -9223372036854774784, maximum: 9223372036854774784 },
        },
        {
          name: 'byColor',
          type: 'object',
          nullable: true,
          constraints: [],
          values: { type: 'integer', nullable: false },
          keys: { type: 'string', nullable: false, enum: ['red', 'sky-blue'], enumExact: ['sky-blue'] },
        },
      ],
    };
    expect(validateInput(schema, { byNumber: { '-5': 'a', '9223372036854774784': 'b' } })).toBeNull();
    expect(validateInput(schema, { byNumber: { '9223372036854774785': 'a' } })).not.toBeNull();
    expect(validateInput(schema, { byNumber: { '01': 'a' } })).not.toBeNull();
    expect(validateInput(schema, { byNumber: { '1e3': 'a' } })).not.toBeNull();
    expect(validateInput(schema, { byNumber: {}, byColor: { RED: 1, 'sky-blue': 2 } })).toBeNull();
    expect(validateInput(schema, { byNumber: {}, byColor: { 'SKY-BLUE': 1 } })).not.toBeNull();
  });
});
