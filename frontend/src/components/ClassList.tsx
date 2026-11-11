import type { ClassInfo } from '../types';

interface Props {
  classes: ClassInfo[] | null;
  /** Classes where this student already has a HELD or CONFIRMED application. */
  activeClassIds: Set<string>;
  hasStudent: boolean;
  busyKey: string | null;
  onApply: (classId: string) => void;
}

export function ClassList({ classes, activeClassIds, hasStudent, busyKey, onApply }: Props) {
  if (classes === null) return <p className="hint">Class-এর তালিকা লোড হচ্ছে…</p>;
  if (classes.length === 0) return <p className="hint">এখনো কোনো class তৈরি করা হয়নি।</p>;

  return (
    <ul className="class-list">
      {classes.map((c) => {
        const applied = activeClassIds.has(c.id);
        const full = c.availableSeats <= 0;
        const disabled = !hasStudent || applied || full || busyKey === `apply:${c.id}`;
        let label = 'আবেদন করুন';
        if (applied) label = 'আবেদন করা হয়েছে';
        else if (full) label = 'Seat নেই';

        return (
          <li key={c.id} className="card class-item" data-testid={`class-${c.id}`}>
            <div className="class-head">
              <h3>{c.name}</h3>
              <button disabled={disabled} onClick={() => onApply(c.id)}>
                {label}
              </button>
            </div>
            <dl className="seat-stats">
              <div>
                <dt>মোট</dt>
                <dd>{c.totalSeats}</dd>
              </div>
              <div>
                <dt>Hold-এ আছে</dt>
                <dd>{c.heldSeats}</dd>
              </div>
              <div>
                <dt>নিশ্চিত</dt>
                <dd>{c.confirmedSeats}</dd>
              </div>
              <div className={full ? 'full' : 'free'}>
                <dt>খালি</dt>
                <dd>{c.availableSeats}</dd>
              </div>
            </dl>
          </li>
        );
      })}
    </ul>
  );
}
