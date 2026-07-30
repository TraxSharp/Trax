// @vitest-environment jsdom
import { act, cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { useMachine } from '../../react';
import { turnstile } from './turnstile';

afterEach(cleanup);

function Gate({ persist }: { persist?: (s: unknown) => void }) {
  const { state, context, send, can, available, lastProblem } = useMachine(turnstile, {
    initial: () => turnstile.initial(),
    persist,
  });
  return (
    <div>
      <p data-testid="state">{state}</p>
      {state === 'Unlocked' && <p data-testid="paid">{context.paidWith}</p>}
      <p data-testid="available">{available.join(',')}</p>
      <button onClick={() => send('Coin', { coin: 'quarter' })} disabled={!can('Coin', { coin: 'quarter' })}>
        coin
      </button>
      <button onClick={() => send('Push')}>push</button>
      {lastProblem && <p role="alert">{lastProblem.message}</p>}
    </div>
  );
}

describe('useMachine', () => {
  it('owns the snapshot, re-renders on a step, and persists after it', () => {
    const persist = vi.fn();
    render(<Gate persist={persist} />);

    expect(screen.getByTestId('state').textContent).toBe('Locked');
    act(() => screen.getByText('coin').click());

    expect(screen.getByTestId('state').textContent).toBe('Unlocked');
    expect(screen.getByTestId('paid').textContent).toBe('quarter');
    expect(persist).toHaveBeenCalledOnce();
  });

  it('surfaces a rejection without throwing and re-renders with the problem', () => {
    render(<Gate />);

    act(() => screen.getByText('push').click()); // no transition out of Locked

    expect(screen.getByTestId('state').textContent).toBe('Locked');
    expect(screen.getByRole('alert')).toBeTruthy();
  });
});
