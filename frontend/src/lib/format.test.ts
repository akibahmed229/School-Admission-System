import { describe, expect, it } from 'vitest';
import { effectiveStatus, formatRemaining, isValidStudentId, remainingMs } from './format';

describe('isValidStudentId', () => {
  it('accepts exactly 17 digits', () => {
    expect(isValidStudentId('20152691234567890')).toBe(true);
    expect(isValidStudentId(' 20152691234567890 ')).toBe(true);
  });
  it('rejects wrong length or non-digits', () => {
    expect(isValidStudentId('')).toBe(false);
    expect(isValidStudentId('2015269123456789')).toBe(false);
    expect(isValidStudentId('201526912345678901')).toBe(false);
    expect(isValidStudentId('2015269123456789a')).toBe(false);
  });
});

describe('countdown', () => {
  it('formats mm:ss and never goes negative', () => {
    expect(formatRemaining(600_000)).toBe('10:00');
    expect(formatRemaining(59_001)).toBe('01:00');
    expect(formatRemaining(0)).toBe('00:00');
    expect(formatRemaining(-5_000)).toBe('00:00');
  });
  it('computes remaining time from an ISO timestamp', () => {
    const now = Date.parse('2026-11-01T04:00:00Z');
    expect(remainingMs('2026-11-01T04:10:00Z', now)).toBe(600_000);
  });
});

describe('effectiveStatus', () => {
  const now = Date.parse('2026-11-01T04:10:00Z');
  it('turns an overdue HELD into EXPIRED without waiting for the server', () => {
    expect(effectiveStatus({ status: 'HELD', expiresAt: '2026-11-01T04:09:59Z' }, now)).toBe('EXPIRED');
  });
  it('keeps a live HELD and never touches other statuses', () => {
    expect(effectiveStatus({ status: 'HELD', expiresAt: '2026-11-01T04:10:01Z' }, now)).toBe('HELD');
    expect(effectiveStatus({ status: 'CONFIRMED', expiresAt: '2020-01-01T00:00:00Z' }, now)).toBe('CONFIRMED');
    expect(effectiveStatus({ status: 'CANCELLED', expiresAt: '2020-01-01T00:00:00Z' }, now)).toBe('CANCELLED');
  });
});
