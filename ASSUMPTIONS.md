# Assumptions and design decisions

The brief leaves several things open. Each decision below says what was chosen and why.

## Concurrency and seat accounting

- **Row lock per class.** Creating a hold runs in one transaction: `SELECT … FROM classes WHERE id = @id FOR UPDATE`
  → expire stale holds of that class → check duplicate → count `HELD + CONFIRMED` → insert. Because the lock
  lives in PostgreSQL (not in application memory or a C# `lock`), it also protects against **two backend
  processes** using the same database (bonus requirement).
- **Why not SERIALIZABLE + retries or a counter column?** A row lock is the simplest thing that is
  obviously correct. Throughput per class is limited to one hold transaction at a time, which is fine for
  a few thousand applicants (each transaction is a few milliseconds) and gives _exactly_ `totalSeats`
  successes, with no retry storms.
- **Confirm does not take the class lock.** `HELD → CONFIRMED` does not change `held + confirmed`, so it
  cannot oversell; it only locks the one hold row so double-confirms and confirm-vs-cancel races serialize.
- **Database-level backstop:** a partial unique index on `(class_id, student_id) WHERE status IN ('HELD','CONFIRMED')`
  guarantees "one active application per student per class" even if application logic were bypassed.
- **Connection pool capped at 40** per backend process (unless the connection string sets its own), so bursts
  queue for a connection instead of exhausting PostgreSQL's default 100 connections.

## Expiry

- A hold is **active** only while `status = 'HELD' AND expires_at > clock_timestamp()`. Nothing has to run
  for a seat to come back: seat counts and `GET` responses compute this on read, using the **database
  clock**, so all backend processes agree and the app's clock is irrelevant.
- When a hold request touches a class, stale `HELD` rows of that class are persisted as `EXPIRED` first
  (keeps the unique index and counts exact). Rows nobody touches stay `HELD` in the table but are
  _reported_ as `EXPIRED`.
- `HOLD_DURATION_SECONDS` (default 600) is read at start-up; invalid or non-positive values fall back to 600.
- `expiresAt` is ISO-8601 UTC with milliseconds (e.g. `2026-11-01T04:10:00.000Z`), computed as
  `now + duration` exactly (no rounding), so the hold lasts the full configured time.

## Identity

- `X-Student-Id` is treated as the identity. **The UI enforces 17 digits; the API only requires a
  non-empty value of at most 64 characters.** Reason: the brief says the value _will be_ a 17-digit number but
  does not define API-side validation, and rejecting a slightly different test identifier would break
  otherwise valid requests. The header is never authenticated (the brief says no login).
- Missing/blank header on endpoints 4, 6, 7, 8 → `400 MISSING_STUDENT_ID`.
- Endpoints 1, 2, 3, 5 do not require the header (the brief lists it only on 4, 6, 7, 8).

## Status codes (the brief only fixes `201`, `200`, `204` and `409` for "no seat")

| Situation                                                                            | Response                                                 |
| ------------------------------------------------------------------------------------ | -------------------------------------------------------- |
| Invalid `POST /classes` body (empty name, `totalSeats` not 1…100000, malformed JSON) | `400`                                                    |
| Unknown class / hold                                                                 | `404`                                                    |
| No seat left                                                                         | `409 NO_SEATS_AVAILABLE`                                 |
| Student already has a `HELD` application in that class                               | `409 ALREADY_APPLIED`                                    |
| Student already has a `CONFIRMED` admission in that class                            | `409 ALREADY_ADMITTED`                                   |
| Confirm/cancel by a different student than the owner                                 | `403 FORBIDDEN`                                          |
| Confirm after expiry / after cancel                                                  | `409 HOLD_EXPIRED` / `409 HOLD_CANCELLED`                |
| Confirm an already confirmed hold                                                    | `200 CONFIRMED` (idempotent, counters unchanged)         |
| Cancel a `HELD` hold                                                                 | `204`                                                    |
| Cancel an already cancelled hold                                                     | `204` (idempotent)                                       |
| Cancel a `CONFIRMED` admission                                                       | `409 CANNOT_CANCEL_CONFIRMED` (refunds are out of scope) |
| Cancel an expired hold                                                               | `409 HOLD_EXPIRED`                                       |

- A student may apply again after a hold **expired or was cancelled**, and may apply to several
  _different_ classes (a shift is a separate class). The "one at a time" rule is per class.
- `GET /holds/{holdId}` does not check the header and does not return `studentId` (it is not in the brief's
  example); ownership is enforced on confirm/cancel, which do take the header.

## Data model

- Each shift is its own class (`"Class 6 - Morning Shift"` and `"Class 6 - Day Shift"` are two classes).
  Class names are not unique; the brief does not ask for it.
- IDs are `c_<12 hex>` / `h_<12 hex>` strings. `AvailableSeats = total − held − confirmed`, computed in one
  SQL statement, so a response is always a consistent snapshot.
- No background job, no cache: PostgreSQL is the only state, so a restart loses nothing.
- Schema is created idempotently on start-up under a PostgreSQL advisory lock (safe when several backend
  processes boot simultaneously). A migration tool was not used to keep the Docker start-up dependency-free.
- No seed data, so automated checks against `GET /classes` start from an empty system.

## Frontend

- Student id is stored in `localStorage` so a refresh or a closed browser keeps the student's applications
  (they are loaded from `GET /me/holds`).
- Countdown uses the server's clock (HTTP `Date` header offset) so a wrong clock on the parent's phone does
  not show a wrong time; status also flips to _EXPIRED_ locally the moment the countdown reaches zero.
- Polling every 3 seconds for seat counts and my applications; polls never overlap.
- All errors, including "server unreachable", are shown as Bangla messages in a visible banner.
- The "payment" button simply calls the confirm endpoint; there is no payment gateway, as stated.
- The backend URL is read at container start-up from `API_BASE_URL` (written into `config.js`), so it can be
  changed without rebuilding the image. It must be a URL reachable from the **browser**.

## Not done / known limits

- No authentication or admin role: anyone can create classes (brief: no login).
- Safety with several backend processes comes from the database row lock; it is not covered by an automated test.
- One class's hold requests are serialized; extremely hot single-class load is bounded by the database, not by CPU.
