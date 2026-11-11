import type { HoldStatus } from '../types';

/** Bangladesh birth registration number: exactly 17 digits. */
export const STUDENT_ID_RE = /^\d{17}$/;

export const isValidStudentId = (value: string): boolean => STUDENT_ID_RE.test(value.trim());

export const STATUS_LABEL: Record<HoldStatus, string> = {
  HELD: 'আবেদন চলমান (seat hold)',
  CONFIRMED: 'ভর্তি নিশ্চিত',
  EXPIRED: 'মেয়াদ শেষ',
  CANCELLED: 'বাতিল',
};

/** Milliseconds left until `expiresAt` (negative when already past). */
export function remainingMs(expiresAt: string, now: number): number {
  return Date.parse(expiresAt) - now;
}

/** mm:ss (or h:mm:ss for long holds). Never negative. */
export function formatRemaining(ms: number): string {
  const total = Math.max(0, Math.ceil(ms / 1000));
  const h = Math.floor(total / 3600);
  const m = Math.floor((total % 3600) / 60);
  const s = total % 60;
  const mm = String(m).padStart(2, '0');
  const ss = String(s).padStart(2, '0');
  return h > 0 ? `${h}:${mm}:${ss}` : `${mm}:${ss}`;
}

/**
 * The server already reports EXPIRED, but polling is only every few seconds. Deriving the status
 * from the clock makes the UI flip to EXPIRED the moment time runs out, without a reload.
 */
export function effectiveStatus(
  hold: { status: HoldStatus; expiresAt: string },
  now: number,
): HoldStatus {
  return hold.status === 'HELD' && Date.parse(hold.expiresAt) <= now ? 'EXPIRED' : hold.status;
}
