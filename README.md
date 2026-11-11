# School Admission System

First-come-first-served online admission for a private school. Each class/shift has a fixed number
of seats. Applying **holds** a seat for a limited time; paying the (simulated) admission fee within
that time **confirms** it; otherwise the seat is released automatically.

**Stack:** .NET 8 (ASP.NET Core minimal API, Npgsql) · PostgreSQL 16 · React 18 + TypeScript + Vite · Docker

```
backend/    ASP.NET Core API + xUnit tests + Dockerfile
frontend/   React UI (served by nginx in Docker) + vitest tests
docker-compose.yml
ASSUMPTIONS.md   decisions where the brief was silent, and why
AI_USAGE.md      which AI tools were used, where, and where they were wrong
```

## Run

Only Docker is required.

```bash
docker compose up --build
```

| What | URL                   |
| ---- | --------------------- |
| UI   | http://localhost:3000 |
| API  | http://localhost:8080 |

Data lives in the named volume `pgdata`, so `docker compose down` followed by `docker compose up`
keeps everything. `docker compose down -v` wipes it.

### Configuration (environment variables)

All have defaults; override them in the shell or a `.env` file (see `.env.example`).

| Variable                               | Default                 | Meaning                                                                                                         |
| -------------------------------------- | ----------------------- | --------------------------------------------------------------------------------------------------------------- |
| `HOLD_DURATION_SECONDS`                | `600`                   | How long a seat is held before it is released                                                                   |
| `ConnectionStrings__DefaultConnection` | set in compose          | PostgreSQL connection string used by the backend                                                                |
| `API_BASE_URL`                         | `http://localhost:8080` | Backend URL used by the **browser**; read by the frontend container at start-up (restart to change, no rebuild) |

Example: `HOLD_DURATION_SECONDS=30 docker compose up --build` to watch expiry quickly.

## Using the UI

1. Enter the student's 17-digit birth registration number in the box at the top (no login).
2. As school admin, create classes with a name and seat count.
3. Press **আবেদন করুন** on a class → the seat is held and a countdown starts under **আমার আবেদন**.
4. **ভর্তি ফি দিন** confirms the admission (no real payment); **বাতিল** cancels and releases the seat.

Seat counts and your applications refresh automatically every 3 seconds; when a countdown reaches
zero the application flips to _মেয়াদ শেষ_ and the pay button is disabled without reloading.

## Tests

### Backend (xUnit, real PostgreSQL)

```bash
docker compose --profile test run --rm backend-tests
```

This starts PostgreSQL, builds the test image and runs the suite. Each run creates and drops its own
throw-away database, so application data is untouched. Highlights:

- **`Concurrent_holds_never_exceed_total_seats`** – 200 parallel requests for 10 seats → exactly 10 succeed, 190 get `409`.
- `Concurrent_holds_by_the_same_student_create_only_one_hold`
- `Confirming_the_same_hold_many_times_counts_the_seat_once`
- `An_expired_hold_frees_its_seat_without_any_api_call_and_cannot_be_confirmed`
- `Two_backend_processes_sharing_one_database_never_oversell` (bonus)

The concurrency tests need a real database on purpose: an in-memory provider has no row locks and
would "pass" without proving anything.

### Frontend (vitest)

```bash
cd frontend
npm ci
npm test
```

## API

Base URL `http://localhost:8080`, JSON in/out. The student is identified by the `X-Student-Id` header.

| #   | Method & path                   | Notes                                                       |
| --- | ------------------------------- | ----------------------------------------------------------- |
| 1   | `POST /classes`                 | `{name, totalSeats}` → `201 {id, name, totalSeats}`         |
| 2   | `GET /classes`                  | list with `heldSeats`, `confirmedSeats`, `availableSeats`   |
| 3   | `GET /classes/{classId}`        | one class                                                   |
| 4   | `POST /classes/{classId}/holds` | header `X-Student-Id` → `201`; `409` if no seat             |
| 5   | `GET /holds/{holdId}`           | `HELD` / `CONFIRMED` / `EXPIRED` / `CANCELLED`              |
| 6   | `POST /holds/{holdId}/confirm`  | header `X-Student-Id` → `200 {holdId, status}` (idempotent) |
| 7   | `DELETE /holds/{holdId}`        | header `X-Student-Id` → `204`                               |
| 8   | `GET /me/holds`                 | header `X-Student-Id`                                       |

Errors are JSON: `{ "error": "NO_SEATS_AVAILABLE", "message": "…" }`. Full list of status codes and
the reasoning behind them is in `ASSUMPTIONS.md`.

## How seats are protected (short version)

Every operation that can change the number of taken seats runs in a PostgreSQL transaction that
first locks the class row (`SELECT … FOR UPDATE`). Requests for one class are serialized by the
database, so `held + confirmed ≤ totalSeats` holds under any load and across multiple backend
processes. Expiry needs no background job: a hold only counts while `expires_at` is in the future,
judged by the database clock. Details in `ASSUMPTIONS.md`.
