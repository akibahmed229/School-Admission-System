import { useEffect, useRef, useState } from 'react';
import { serverNow } from '../api';

/** Server-corrected "now", re-rendering every second (drives the countdowns). */
export function useNow(intervalMs = 1000): number {
  const [now, setNow] = useState(() => serverNow());
  useEffect(() => {
    const id = window.setInterval(() => setNow(serverNow()), intervalMs);
    return () => window.clearInterval(id);
  }, [intervalMs]);
  return now;
}

/** Runs `fn` now and then every `intervalMs` while `enabled`. Never overlaps itself. */
export function usePolling(fn: () => Promise<void>, intervalMs: number, enabled: boolean): void {
  const fnRef = useRef(fn);
  fnRef.current = fn;

  useEffect(() => {
    if (!enabled) return;
    let stopped = false;
    let running = false;

    const tick = async () => {
      if (stopped || running) return;
      running = true;
      try {
        await fnRef.current();
      } finally {
        running = false;
      }
    };

    void tick();
    const id = window.setInterval(() => void tick(), intervalMs);
    return () => {
      stopped = true;
      window.clearInterval(id);
    };
  }, [intervalMs, enabled]);
}
