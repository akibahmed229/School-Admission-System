import type { ClassInfo, MyHold } from './types';

declare global {
  interface Window {
    __APP_CONFIG__?: { API_BASE_URL?: string };
  }
}

function baseUrl(): string {
  const url =
    window.__APP_CONFIG__?.API_BASE_URL ||
    (import.meta.env.VITE_API_BASE_URL as string | undefined) ||
    'http://localhost:8080';
  return url.replace(/\/+$/, '');
}

export class ApiError extends Error {
  constructor(
    public status: number,
    public code: string,
    message: string,
  ) {
    super(message);
  }
}

// Difference between the server clock and this browser's clock (from the HTTP Date header),
// so the countdown does not drift when the user's PC clock is wrong.
let serverOffsetMs = 0;
export const serverNow = (): number => Date.now() + serverOffsetMs;

interface RequestOptions {
  method?: 'GET' | 'POST' | 'DELETE';
  studentId?: string;
  body?: unknown;
}

async function request<T>(path: string, opts: RequestOptions = {}): Promise<T> {
  const headers: Record<string, string> = {};
  if (opts.studentId) headers['X-Student-Id'] = opts.studentId;
  if (opts.body !== undefined) headers['Content-Type'] = 'application/json';

  let res: Response;
  try {
    res = await fetch(`${baseUrl()}${path}`, {
      method: opts.method ?? 'GET',
      headers,
      body: opts.body !== undefined ? JSON.stringify(opts.body) : undefined,
    });
  } catch {
    throw new ApiError(0, 'NETWORK', 'Cannot reach the server');
  }

  const dateHeader = res.headers.get('Date');
  if (dateHeader) {
    const t = Date.parse(dateHeader);
    if (!Number.isNaN(t)) serverOffsetMs = t - Date.now();
  }

  if (res.status === 204) return undefined as T;

  const text = await res.text();
  let data: { error?: string; message?: string } | null = null;
  try {
    data = text ? JSON.parse(text) : null;
  } catch {
    data = null;
  }

  if (!res.ok) {
    throw new ApiError(res.status, data?.error ?? 'UNKNOWN', data?.message ?? res.statusText);
  }
  return data as T;
}

export const api = {
  listClasses: () => request<ClassInfo[]>('/classes'),
  createClass: (name: string, totalSeats: number) =>
    request<{ id: string; name: string; totalSeats: number }>('/classes', {
      method: 'POST',
      body: { name, totalSeats },
    }),
  apply: (classId: string, studentId: string) =>
    request<{ holdId: string }>(`/classes/${encodeURIComponent(classId)}/holds`, {
      method: 'POST',
      studentId,
    }),
  confirm: (holdId: string, studentId: string) =>
    request<{ holdId: string; status: string }>(`/holds/${encodeURIComponent(holdId)}/confirm`, {
      method: 'POST',
      studentId,
    }),
  cancel: (holdId: string, studentId: string) =>
    request<void>(`/holds/${encodeURIComponent(holdId)}`, { method: 'DELETE', studentId }),
  myHolds: (studentId: string) => request<MyHold[]>('/me/holds', { studentId }),
};
