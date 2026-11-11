import type { MyHold } from '../types';
import { STATUS_LABEL, effectiveStatus, formatRemaining, remainingMs } from '../lib/format';

interface Props {
  hold: MyHold;
  now: number;
  busy: boolean;
  onPay: (holdId: string) => void;
  onCancel: (holdId: string) => void;
}

export function HoldCard({ hold, now, busy, onPay, onCancel }: Props) {
  const status = effectiveStatus(hold, now);
  const isHeld = status === 'HELD';

  return (
    <li className={`card hold-item status-${status.toLowerCase()}`} data-testid={`hold-${hold.holdId}`}>
      <div className="class-head">
        <h3>{hold.className}</h3>
        <span className={`badge badge-${status.toLowerCase()}`} data-testid="status">
          {STATUS_LABEL[status]}
        </span>
      </div>

      {isHeld && (
        <p className="countdown">
          মেয়াদ শেষ হতে বাকি: <strong data-testid="countdown">{formatRemaining(remainingMs(hold.expiresAt, now))}</strong>
          <span className="hint"> — এর মধ্যে ভর্তি ফি না দিলে seat অন্যের জন্য খুলে যাবে।</span>
        </p>
      )}
      {status === 'EXPIRED' && (
        <p className="hint">মেয়াদ শেষ হয়ে গেছে, seat ছেড়ে দেওয়া হয়েছে। চাইলে আবার আবেদন করতে পারেন।</p>
      )}
      {status === 'CONFIRMED' && <p className="ok">ভর্তি ফি জমা হয়েছে, আপনার seat নিশ্চিত।</p>}

      <div className="row">
        <button disabled={!isHeld || busy} onClick={() => onPay(hold.holdId)}>
          ভর্তি ফি দিন
        </button>
        <button className="secondary" disabled={!isHeld || busy} onClick={() => onCancel(hold.holdId)}>
          বাতিল
        </button>
      </div>
    </li>
  );
}
