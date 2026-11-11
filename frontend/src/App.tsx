import { useCallback, useMemo, useState } from 'react';
import { ApiError, api } from './api';
import type { ClassInfo, MyHold } from './types';
import { describeError } from './lib/errors';
import { effectiveStatus, isValidStudentId } from './lib/format';
import { useNow, usePolling } from './lib/hooks';
import { StudentIdBar } from './components/StudentIdBar';
import { CreateClassForm } from './components/CreateClassForm';
import { ClassList } from './components/ClassList';
import { HoldCard } from './components/HoldCard';

const POLL_MS = 3000;
const STORAGE_KEY = 'school-admission:studentId';

function loadStudentId(): string {
  try {
    const v = window.localStorage.getItem(STORAGE_KEY) ?? '';
    return isValidStudentId(v) ? v : '';
  } catch {
    return '';
  }
}

type Notice = { kind: 'error' | 'success'; text: string } | null;

export default function App() {
  const [studentId, setStudentId] = useState<string>(loadStudentId);
  const [classes, setClasses] = useState<ClassInfo[] | null>(null);
  const [holds, setHolds] = useState<MyHold[] | null>(null);
  const [offline, setOffline] = useState(false);
  const [notice, setNotice] = useState<Notice>(null);
  const [busyKey, setBusyKey] = useState<string | null>(null);
  const now = useNow();

  const refreshClasses = useCallback(async () => {
    try {
      setClasses(await api.listClasses());
      setOffline(false);
    } catch (e) {
      setOffline(e instanceof ApiError && e.code === 'NETWORK');
    }
  }, []);

  const refreshHolds = useCallback(async () => {
    if (!studentId) return;
    try {
      setHolds(await api.myHolds(studentId));
    } catch {
      /* the classes poll already reports connectivity problems */
    }
  }, [studentId]);

  // Seat numbers and my applications refresh by themselves every few seconds.
  usePolling(refreshClasses, POLL_MS, true);
  usePolling(refreshHolds, POLL_MS, studentId !== '');

  const saveStudentId = (id: string) => {
    try {
      window.localStorage.setItem(STORAGE_KEY, id);
    } catch {
      /* private mode: keep it in memory only */
    }
    setHolds(null);
    setStudentId(id);
    setNotice(null);
  };

  const run = async (key: string, action: () => Promise<string | void>) => {
    setBusyKey(key);
    setNotice(null);
    try {
      const success = await action();
      if (success) setNotice({ kind: 'success', text: success });
    } catch (e) {
      setNotice({ kind: 'error', text: describeError(e) });
    } finally {
      setBusyKey(null);
      void refreshClasses();
      void refreshHolds();
    }
  };

  const onCreateClass = async (name: string, totalSeats: number): Promise<boolean> => {
    let ok = false;
    await run('create', async () => {
      await api.createClass(name, totalSeats);
      ok = true;
      return `"${name}" class তৈরি হয়েছে।`;
    });
    return ok;
  };

  const onApply = (classId: string) =>
    run(`apply:${classId}`, async () => {
      await api.apply(classId, studentId);
      return 'আবেদন সফল হয়েছে! seat ধরে রাখা হয়েছে — সময় শেষ হওয়ার আগে ভর্তি ফি দিন।';
    });

  const onPay = (holdId: string) =>
    run(`pay:${holdId}`, async () => {
      await api.confirm(holdId, studentId);
      return 'ভর্তি ফি জমা হয়েছে, ভর্তি নিশ্চিত!';
    });

  const onCancel = (holdId: string) =>
    run(`cancel:${holdId}`, async () => {
      await api.cancel(holdId, studentId);
      return 'আবেদন বাতিল করা হয়েছে।';
    });

  const activeClassIds = useMemo(() => {
    const ids = new Set<string>();
    for (const h of holds ?? []) {
      const s = effectiveStatus(h, now);
      if (s === 'HELD' || s === 'CONFIRMED') ids.add(h.classId);
    }
    return ids;
  }, [holds, now]);

  return (
    <div className="app">
      <header>
        <h1>School Admission System</h1>
        <p className="hint">আগে আবেদন করলে আগে seat — ভর্তি ফি দিলে ভর্তি নিশ্চিত।</p>
      </header>

      <StudentIdBar key={studentId} studentId={studentId} onSave={saveStudentId} />

      {offline && (
        <div className="banner error" role="alert">
          সার্ভারের সাথে সংযোগ বিচ্ছিন্ন। সংযোগ ফিরলে তথ্য নিজে থেকেই হালনাগাদ হবে।
        </div>
      )}
      {notice && (
        <div className={`banner ${notice.kind}`} role={notice.kind === 'error' ? 'alert' : 'status'}>
          <span>{notice.text}</span>
          <button className="link" onClick={() => setNotice(null)} aria-label="বার্তা বন্ধ করুন">
            ✕
          </button>
        </div>
      )}

      <main className="columns">
        <section>
          <h2>Class তালিকা</h2>
          <CreateClassForm busy={busyKey === 'create'} onCreate={onCreateClass} />
          <ClassList
            classes={classes}
            activeClassIds={activeClassIds}
            hasStudent={studentId !== ''}
            busyKey={busyKey}
            onApply={onApply}
          />
        </section>

        <section>
          <h2>আমার আবেদন</h2>
          {!studentId && <p className="hint">জন্ম নিবন্ধন নম্বর দিলে আপনার আবেদনগুলো এখানে দেখা যাবে।</p>}
          {studentId && holds === null && <p className="hint">লোড হচ্ছে…</p>}
          {studentId && holds !== null && holds.length === 0 && (
            <p className="hint">এই নম্বর দিয়ে এখনো কোনো আবেদন করা হয়নি।</p>
          )}
          <ul className="hold-list">
            {(holds ?? []).map((h) => (
              <HoldCard
                key={h.holdId}
                hold={h}
                now={now}
                busy={busyKey !== null && busyKey.endsWith(h.holdId)}
                onPay={onPay}
                onCancel={onCancel}
              />
            ))}
          </ul>
        </section>
      </main>
    </div>
  );
}
