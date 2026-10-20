import { describe, expect, it } from 'vitest';
import { turnstile } from './turnstile';

/**
 * The real assertions here are the `@ts-expect-error` lines: they are validated by `tsc --noEmit`
 * (the `typecheck` script), which fails if any expected type error does NOT occur. vitest runs the file
 * to keep it honest at runtime too.
 */
describe('typed machine compile-time safety', () => {
  const s = turnstile.initial();

  it('accepts valid, fully-typed usage', () => {
    const result = turnstile.advance(s, 'Coin', { coin: 'quarter' });
    expect(result.outcome).toBe('transitioned');
    expect(turnstile.can(s, 'Coin', { coin: 'quarter' })).toBe(true);
  });

  it('rejects invalid usage at compile time', () => {
    // @ts-expect-error unknown trigger
    turnstile.advance(s, 'Nope');
    // @ts-expect-error missing required input
    turnstile.advance(s, 'Coin');
    // @ts-expect-error forbidden input on a no-input trigger
    turnstile.advance(s, 'Push', { anything: 1 });
    // @ts-expect-error wrong input shape
    turnstile.advance(s, 'Coin', { coin: 123 });

    expect(true).toBe(true);
  });
});
