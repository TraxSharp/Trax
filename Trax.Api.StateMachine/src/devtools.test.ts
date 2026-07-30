import { describe, expect, it, vi } from 'vitest';
import { createDevLogger } from './devtools';
import { turnstile, type TurnstileSpec } from './machines/turnstile/turnstile';

const snap = turnstile.initial();
const fakeConsole = () => ({ log: vi.fn(), warn: vi.fn(), error: vi.fn() });

describe('dev logger', () => {
  it('logs transitions and warns on rejections', () => {
    const c = fakeConsole();
    const logger = createDevLogger<TurnstileSpec>('turnstile', { console: c });

    logger.onTransition?.('Locked', 'Coin', 'Unlocked', snap);
    logger.onRejected?.('Push', { kind: 'rejected', code: 'no-transition', message: 'nope' }, snap);

    expect(c.log).toHaveBeenCalledWith('[state-machine:turnstile] Locked --Coin--> Unlocked');
    expect(c.warn).toHaveBeenCalledOnce();
  });

  it('errors loudly on an internal error and throws under strict', () => {
    const c = fakeConsole();
    const strict = createDevLogger<TurnstileSpec>('x', { console: c, strict: true });

    expect(() => strict.onInternalError?.('Coin', 'boom', snap)).toThrow(/internal error/);
    expect(c.error).toHaveBeenCalledOnce();
  });

  it('does not throw on an internal error when not strict', () => {
    const c = fakeConsole();
    const lenient = createDevLogger<TurnstileSpec>('x', { console: c });

    expect(() => lenient.onInternalError?.('Coin', 'boom', snap)).not.toThrow();
    expect(c.error).toHaveBeenCalledOnce();
  });
});
