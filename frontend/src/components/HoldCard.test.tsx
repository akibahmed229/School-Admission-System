import { afterEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { HoldCard } from './HoldCard';
import type { MyHold } from '../types';

afterEach(cleanup);

const hold: MyHold = {
  holdId: 'h_1',
  classId: 'c_1',
  className: 'Class 6 - Morning Shift',
  status: 'HELD',
  expiresAt: '2026-11-01T04:10:00Z',
};
const T = (iso: string) => Date.parse(iso);

describe('HoldCard', () => {
  it('shows a countdown and lets the student pay while the hold is live', () => {
    const onPay = vi.fn();
    render(<HoldCard hold={hold} now={T('2026-11-01T04:08:00Z')} busy={false} onPay={onPay} onCancel={vi.fn()} />);

    expect(screen.getByTestId('countdown').textContent).toBe('02:00');
    const pay = screen.getByRole('button', { name: 'ভর্তি ফি দিন' }) as HTMLButtonElement;
    expect(pay.disabled).toBe(false);
    fireEvent.click(pay);
    expect(onPay).toHaveBeenCalledWith('h_1');
  });

  it('flips to EXPIRED and disables payment as soon as time runs out, with no reload', () => {
    render(<HoldCard hold={hold} now={T('2026-11-01T04:10:01Z')} busy={false} onPay={vi.fn()} onCancel={vi.fn()} />);

    expect(screen.getByTestId('status').textContent).toBe('মেয়াদ শেষ');
    expect(screen.queryByTestId('countdown')).toBeNull();
    expect((screen.getByRole('button', { name: 'ভর্তি ফি দিন' }) as HTMLButtonElement).disabled).toBe(true);
    expect((screen.getByRole('button', { name: 'বাতিল' }) as HTMLButtonElement).disabled).toBe(true);
  });

  it('disables actions for a confirmed admission', () => {
    render(
      <HoldCard
        hold={{ ...hold, status: 'CONFIRMED' }}
        now={T('2026-11-01T04:01:00Z')}
        busy={false}
        onPay={vi.fn()}
        onCancel={vi.fn()}
      />,
    );
    expect(screen.getByTestId('status').textContent).toBe('ভর্তি নিশ্চিত');
    expect((screen.getByRole('button', { name: 'ভর্তি ফি দিন' }) as HTMLButtonElement).disabled).toBe(true);
  });
});
