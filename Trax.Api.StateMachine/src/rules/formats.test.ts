import { describe, expect, it } from 'vitest';
import { isOneOf, matchesFormat } from './formats';
import { validateInput, type ContextSchema } from './interpreter';

// A string field is as strict as its C# type: an enum's names, and the formats of a Guid, a date, a time, a
// duration, a URI and a char. The mirror of the C# SchemaFormatTests; the C# StrictInputParityTests sends its whole
// case table through both engines.

describe('formats', () => {
  it.each([
    ['uuid', '6F9619FF-8B86-D011-B42D-00C04FC964FF', true],
    ['uuid', '6f9619ff8b86d011b42d00c04fc964ff', false],
    ['uuid', '{6f9619ff-8b86-d011-b42d-00c04fc964ff}', false],
    ['date-time', '2026-10-12T10:00:00.1234567+02:00', true],
    ['date-time', '2026-10-12T10:00:00Z', true],
    ['date-time', '2026-10-12T10:00:00+14:30', false],
    ['date-time', '2026-02-30T10:00:00', false],
    ['date-time', '2026-12-31T23:59:60Z', false],
    ['date-time', '2026-10-1２T10:00:00', false],
    ['date-time', '0001-01-01T00:00:00', true],
    ['date-time', '0001-01-01T00:59:59+01:00', false],
    ['date-time', '9999-12-31T23:59:59.9999999-01:00', false],
    ['date-time', '9999-12-31T23:59:59.9999999+01:00', true],
    ['date-time-offset', '0001-01-01T00:00:00Z', true],
    ['date-time-offset', '0001-01-01T00:00:00+01:00', false],
    ['date-time-offset', '0001-01-01T23:00:00', false],
    ['date-time-offset', '9999-12-31T00:00:00', false],
    ['date-time-offset', '9999-12-30T23:59:59', true],
    ['date', '2000-02-29', true],
    ['date', '1900-02-29', false],
    ['date', '0000-01-01', false],
    ['time', '23:59:59.5', true],
    ['time', '12:30', false],
    ['duration', 'P1DT2H30M', true],
    ['duration', 'PT٥M', false],
    ['duration', '00:05:00', false],
    ['duration', 'P10675199DT2H48M5.4775807S', true],
    ['duration', 'P10675199DT2H48M5.4775808S', false],
    ['uri', 'urn:isbn:0451450523', true],
    ['uri', '/a/b', false],
    ['uri', 'https://example.com/a b', false],
    ['uri', 'ab:', true],
    ['uri', 'https:', false],
    ['uri', 'c:foo', false],
    ['uri', 'http://[::1', false],
    ['uri', 'http://[::1]:8080/a', true],
    ['uri', 'http://[1::2::3]/', false],
    ['uri', 'http://[::ffff:1.2.3.4]/', true],
    ['uri', 'http://a:99999/', false],
    ['uri', 'http://a:65535/a?b=c#d', true],
    ['uri', 'http://a..b/', false],
    ['uri', 'http://1.2.3.999/', false],
    ['uri', 'http://u@a/', false],
    ['uri', 'file:///tmp/a', true],
    ['uri', 'file:////share/a', false],
    ['uri', 'file:///a:x', false],
    ['uri', 'mailto:a.b@example.com', true],
    ['uri', 'mailto:a', false],
    ['uri', 'net.pipe://a:1/', false],
    ['uri', 'http://a/%zz', false],
    ['char', 'é', true],
    ['char', '😀', false],
    ['char', '', false],
  ])('%s %j is %s', (format, value, valid) => {
    expect(matchesFormat(format, value)).toBe(valid);
  });

  it('compares enum names ignoring ASCII case only, and a renamed member exactly', () => {
    const names = ['red', 'darkGreen', 'sky-blue'];
    const exact = ['sky-blue'];
    expect(isOneOf('DarkGreen', names, exact)).toBe(true);
    expect(isOneOf('sky-blue', names, exact)).toBe(true);
    expect(isOneOf('SKY-BLUE', names, exact)).toBe(false);
    expect(isOneOf('purple', names, exact)).toBe(false);
    expect(isOneOf('skyBlue', names, exact)).toBe(false);
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
