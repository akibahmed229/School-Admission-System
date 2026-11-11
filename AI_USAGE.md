# AI usage

## Tools used

- Claude (Anthropic), via the claude.ai chat interface.

## What it was used for

- Reading the assignment brief and comparing it with a previous assessment from the same company, to see what had changed.
- Generating the first draft of the backend (`backend/SchoolAdmission.Api`), the backend tests, the Dockerfiles and `docker-compose.yml`, the React frontend, and drafts of `README.md` and `ASSUMPTIONS.md`.
- Explaining the backend to me (I am new to .NET), helping with Docker and git problems, and helping me trim the test suite to the three tests the brief requires.

The AI could not compile or run the .NET code in its own environment and told me so. I ran everything myself and reported the results back, so the problems below were found by running the project.

## Where the AI was wrong, and how it was caught

1. **Missing `using` in the tests.**
   - *What the AI produced:* `SeatHoldTests.cs` called `PostAsJsonAsync` without `using System.Net.Http.Json;`.
   - *How I noticed:* `docker compose --profile test run --rm backend-tests` failed with three `CS1061` compile errors.
   - *What I changed:* added the `using`. The error then stayed identical, with the same line numbers, because `docker compose run` reused the old test image (the AI's first instruction left out the rebuild). I fixed that by running `docker compose --profile test build backend-tests` before each test run.

2. **The UI hid errors and stayed on "loading".**
   - *What the AI produced:* a frontend that, when the API call failed in any way other than a network error (or returned something that was not JSON), silently ignored it, so the class list stayed on "loading…" forever with no message. The brief forbids a blank page or errors only in the console.
   - *How I noticed:* the page stayed stuck, and the backend container log showed the page requests but no API requests at all, so the UI was not getting data and was not saying why.
   - *What I changed:* every failure in the request layer is now a typed error, a 2xx response that is not JSON is treated as an error, and load failures show a readable Bangla banner that names the backend URL. The list shows "load failed" instead of "loading".

3. **Redundant port setting.**
   - *What the AI produced:* an extra `ASPNETCORE_URLS` setting in the Dockerfile and compose file.
   - *How I noticed:* the backend start-up log warned "Overriding HTTP_PORTS '8080'".
   - *What I changed:* removed the setting; the .NET 8 image already listens on 8080.

## What I verified myself

- Ran `docker compose up --build`: PostgreSQL became healthy, the backend created the schema and listened on port 8080, and the frontend was served on port 3000.
- Ran the backend test suite (first attempt failed to compile, see above).
- Decided to reduce the tests to the three the brief requires (a concurrency test, one active application per student, idempotent confirm) and updated the README and ASSUMPTIONS.md to match.
