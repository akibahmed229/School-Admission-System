import { useState } from 'react';

interface Props {
  busy: boolean;
  onCreate: (name: string, totalSeats: number) => Promise<boolean>;
}

export function CreateClassForm({ busy, onCreate }: Props) {
  const [name, setName] = useState('');
  const [seats, setSeats] = useState('');
  const [error, setError] = useState('');

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    const trimmed = name.trim();
    const n = Number(seats);
    if (!trimmed) return setError('Class-এর নাম লিখুন (যেমন "Class 6 - Morning Shift")।');
    if (!Number.isInteger(n) || n < 1) return setError('Seat সংখ্যা ১ বা তার বেশি পূর্ণসংখ্যা হতে হবে।');
    setError('');
    if (await onCreate(trimmed, n)) {
      setName('');
      setSeats('');
    }
  };

  return (
    <form className="card create-form" onSubmit={submit}>
      <h2>নতুন class তৈরি (school admin)</h2>
      <div className="row">
        <input
          aria-label="Class-এর নাম"
          placeholder="Class 6 - Morning Shift"
          value={name}
          maxLength={100}
          onChange={(e) => setName(e.target.value)}
        />
        <input
          aria-label="মোট seat"
          type="number"
          min={1}
          placeholder="Seat"
          value={seats}
          onChange={(e) => setSeats(e.target.value)}
          className="seats-input"
        />
        <button type="submit" disabled={busy}>
          তৈরি করুন
        </button>
      </div>
      {error && (
        <p className="field-error" role="alert">
          {error}
        </p>
      )}
    </form>
  );
}
