import { describe, expect, it } from 'vitest';
import { RejectionReasons } from '../results';
import { idle, rangeCore, validRange } from './testing/rangeMachine';

// Trigger input is validated against the schema the trigger declares, in both engines, before any guard runs:
// a bad input is refused as `invalid-input` and never throws. Tested through the public engine (`advance`), the
// TypeScript mirror of the C# trigger-input validation tests.

const refused = { outcome: 'rejected', reason: RejectionReasons.InvalidInput };
const advanceSet = (input: unknown) => rangeCore.advance(idle, 'Set', input);

describe('the range machine the validation tests use', () => {
  it('accepts a valid input on every trigger', () => {
    expect(advanceSet(validRange())).toMatchObject({ outcome: 'transitioned' });
    expect(
      rangeCore.advance(idle, 'Schedule', { window: { start: 1, end: 2 }, slots: [{ at: 1 }] }),
    ).toMatchObject({ outcome: 'transitioned' });
    expect(rangeCore.advance(idle, 'Ping')).toMatchObject({ outcome: 'transitioned' });
  });
});

describe('validateInput (through advance)', () => {
  it('validateInput refuses a missing required input field', () => {
    expect(advanceSet({ to: 2, tags: ['a'] })).toMatchObject(refused);
  });

  it('validateInput refuses a number sent as a string', () => {
    expect(advanceSet({ ...validRange(), from: '2020' })).toMatchObject(refused);
  });

  it('validateInput refuses an unknown input field', () => {
    expect(advanceSet({ ...validRange(), x: 1 })).toMatchObject(refused);
  });

  it('validateInput refuses a field named in PascalCase as an unknown field, not an alias', () => {
    expect(advanceSet({ From: 1, to: 2, tags: ['a'] })).toMatchObject(refused);
  });

  it('validateInput refuses a non-object input', () => {
    for (const input of ['s', 1, [], true, null]) expect(advanceSet(input), JSON.stringify(input)).toMatchObject(refused);
  });

  it('validateInput refuses an input that breaks a constraint, checked against the input not the context', () => {
    // The context's tags are non-empty; the input's are not.
    expect(idle.context.tags).toEqual(['seed']);
    expect(advanceSet({ ...validRange(), tags: [] })).toMatchObject(refused);
  });

  it('validateInput refuses unknown fields on a trigger that declares no input, and accepts null and {}', () => {
    expect(rangeCore.advance(idle, 'Ping', null)).toMatchObject({ outcome: 'transitioned' });
    expect(rangeCore.advance(idle, 'Ping', {})).toMatchObject({ outcome: 'transitioned' });
    expect(rangeCore.advance(idle, 'Ping', { x: 1 })).toMatchObject(refused);
  });

  it('validateInput refuses a fraction in an integer field and accepts an integral value', () => {
    // 2020.0 and 2020 are the same JavaScript number, so the verdict is decided by the value.
    expect(advanceSet(JSON.parse('{"from":2020.0,"to":2021,"tags":["a"]}'))).toMatchObject({
      outcome: 'transitioned',
    });
    expect(advanceSet({ ...validRange(), from: 1.5 })).toMatchObject(refused);
  });

  it('validateInput refuses a number beyond double range, the same verdict as the C# engine', () => {
    // JSON.parse reads 1e400 as Infinity; C# keeps the text. Both refuse it.
    const input = JSON.parse('{"from":1e400,"to":2,"tags":["a"]}') as Record<string, unknown>;
    expect(input.from).toBe(Infinity);
    expect(advanceSet(input)).toMatchObject(refused);
  });

  it('validateInput refuses a lone surrogate in an input string, not as an internal error', () => {
    expect(advanceSet({ ...validRange(), note: '\uD800' })).toMatchObject(refused);
  });

  it('validateInput refuses a nested record input as strictly as its declared shape', () => {
    // A wrong-kind leaf inside the nested record, and inside an element of a list of records.
    expect(
      rangeCore.advance(idle, 'Schedule', { window: { start: '1', end: 2 }, slots: [{ at: 1 }] }),
    ).toMatchObject(refused);
    expect(
      rangeCore.advance(idle, 'Schedule', { window: { start: 1, end: 2 }, slots: [{ at: 'x' }] }),
    ).toMatchObject(refused);
    expect(
      rangeCore.advance(idle, 'Schedule', { window: { start: 1, end: 2, extra: true }, slots: [] }),
    ).toMatchObject(refused);
  });
});
