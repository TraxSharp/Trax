import { describe, expect, it, vi } from 'vitest';
import { MachineController, type ControllerOptions } from '../../controller';
import { turnstile, type TurnstileSpec } from './turnstile';

const make = (opts: Partial<ControllerOptions<TurnstileSpec>> = {}) =>
  new MachineController<TurnstileSpec>(turnstile, { initial: () => turnstile.initial(), ...opts });

describe('MachineController', () => {
  it('transitions, persists after the step, and notifies once on a successful send', () => {
    const persist = vi.fn();
    const listener = vi.fn();
    const c = make({ persist });
    c.subscribe(listener);

    const moved = c.send('Coin', { coin: 'quarter' });

    expect(moved).toBe(true);
    expect(c.state).toBe('Unlocked');
    expect(c.context).toEqual({ paidWith: 'quarter' });
    expect(persist).toHaveBeenCalledOnce();
    expect(listener).toHaveBeenCalledOnce();
    expect(c.lastProblem).toBeNull();
  });

  it('surfaces a rejection without changing state, STILL notifies, and returns false', () => {
    const persist = vi.fn();
    const listener = vi.fn();
    const c = make({ persist });
    c.subscribe(listener);

    const moved = c.send('Push'); // no transition out of Locked

    expect(moved).toBe(false);
    expect(c.state).toBe('Locked');
    expect(c.lastProblem?.code).toBe('no-transition');
    expect(listener).toHaveBeenCalledOnce(); // a declined action is never dropped
    expect(persist).not.toHaveBeenCalled();
    // a new view object is emitted so useSyncExternalStore re-renders
    expect(c.getSnapshot().problem).not.toBeNull();
  });

  it('reset clears the problem and returns to the initial snapshot', () => {
    const c = make();
    c.send('Push');
    expect(c.lastProblem).not.toBeNull();

    c.reset();

    expect(c.lastProblem).toBeNull();
    expect(c.state).toBe('Locked');
  });

  it('can/available reflect guards and wiring', () => {
    const c = make();
    expect(c.can('Coin', { coin: 'quarter' })).toBe(true);
    expect(c.can('Coin', { coin: 'penny' })).toBe(false);
    expect(c.available()).toEqual(['Coin']);
  });
});
