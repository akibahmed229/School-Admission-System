import { useState } from 'react';
import { isValidStudentId } from '../lib/format';

interface Props {
  studentId: string;
  onSave: (id: string) => void;
}

export function StudentIdBar({ studentId, onSave }: Props) {
  const [draft, setDraft] = useState(studentId);
  const [error, setError] = useState('');

  const submit = (e: React.FormEvent) => {
    e.preventDefault();
    const value = draft.trim();
    if (!isValidStudentId(value)) {
      setError('জন্ম নিবন্ধন নম্বর ঠিক ১৭টি অঙ্কের হতে হবে (শুধু ইংরেজি সংখ্যা)।');
      return;
    }
    setError('');
    onSave(value);
  };

  return (
    <form className="student-bar" onSubmit={submit} aria-label="শিক্ষার্থীর পরিচয়">
      <label htmlFor="student-id">শিক্ষার্থীর জন্ম নিবন্ধন নম্বর (১৭ অঙ্ক)</label>
      <div className="row">
        <input
          id="student-id"
          inputMode="numeric"
          autoComplete="off"
          maxLength={17}
          placeholder="যেমন: 20152691234567890"
          value={draft}
          onChange={(e) => setDraft(e.target.value.replace(/\D/g, ''))}
          aria-invalid={error !== ''}
        />
        <button type="submit">{studentId === draft.trim() && studentId ? 'সংরক্ষিত ✓' : 'ব্যবহার করুন'}</button>
      </div>
      {error && (
        <p className="field-error" role="alert">
          {error}
        </p>
      )}
      {!studentId && !error && <p className="hint">আবেদন করতে আগে নম্বরটি দিন। কোনো login লাগবে না।</p>}
    </form>
  );
}
