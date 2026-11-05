# BookingApp

A local meeting-room booking app built with ASP.NET Core 10, SQLite, EF Core, and a small JavaScript frontend. All booking operations go through the API. The SQLite native engine is bundled through NuGet; a separate SQLite installation is not required.

## Requirements

- .NET 10 SDK 
- Playwright Chromium is required only for the browser interaction test.

## Run locally

```powershell
dotnet restore
dotnet run --project src/BookingApp.Api
```

Open the local URL printed by `dotnet run`. On first start, the API applies database migrations and seeds Vikas, Aashima, and Riva rooms. The default database is `src/BookingApp.Api/booking-app.db`; override it with the `DatabasePath` configuration value (for example, environment variable `DatabasePath=C:\data\booking.db`).

In Development, interactive API docs are available at `/swagger` and the generated OpenAPI document is at `/swagger/v1/swagger.json`.

## Run tests

```powershell
dotnet test
```

The frontend test installs Playwright Chromium on first run if it is missing (network access is needed once). The API integration tests use an isolated temporary SQLite database per test.

## API summary

- `GET /api/rooms`
- `GET /api/bookings?date=YYYY-MM-DD`
- `GET /api/rooms/{roomId}/bookings?date=YYYY-MM-DD`
- `POST /api/bookings` with `roomId`, `name`, `date`, `startTime`, `endTime`
- `DELETE /api/bookings/{bookingId}`

All dates and times are UTC. Request times use `HH:mm` with minute precision. See [the specification](docs/spec.md) for validation, status codes, response shapes, and acceptance criteria.

## Concurrency approach

Booking creation opens a SQLite immediate write transaction before checking availability. SQLite serializes writers, so when requests compete, the second request waits for the first transaction, then checks against its committed booking and returns `409 Conflict`. The overlap check and insert happen within that same transaction. The API test sends two competing requests and checks that exactly one booking is saved.

This serializes writes across the whole SQLite database, including independent rooms. That is a sensible tradeoff for this local, low-volume app. If write throughput or multiple app instances became important, I would move to PostgreSQL and enforce non-overlap with a database exclusion constraint scoped to the room.

## Migrations

EF Core migrations are source-controlled under `src/BookingApp.Api/Migrations` and applied at startup. To create a migration after changing the data model:

```powershell
dotnet tool restore
dotnet ef migrations add DescribeTheChange --project src/BookingApp.Api --startup-project src/BookingApp.Api
```

Room seeding is idempotent and runs after migrations.

## Tradeoffs and next improvements

- SQLite is easy to run locally and keeps the repository self-contained, but it serializes all writes.
- The API stores UTC `DateTime` values and constructs them from the supplied date and `HH:mm` values, independent of the host timezone.
- A next step for a deployed, multi-instance app would be PostgreSQL with a database-level exclusion constraint, plus structured logging and a deployment-specific migration step.

## AI and library disclosure

OpenAI Codex's assistance was used for spec driven development using [the specification](docs/spec.md) and drafting this README.md file. The app uses ASP.NET Core, EF Core SQLite, xUnit, ASP.NET Core `WebApplicationFactory`, and Microsoft Playwright, all distributed as NuGet packages.
The following changes were implemented manually:
- Updated the Program.cs to include the app.MapMethods section, to ensure that the calls to unkown "/api/.." URLs do not return the frontend HTML.
- Updated the MapDelete endpoint to use bookingIdText instead of bookingId:int to allow passing string/text from the UI and API calls and implemented validation and parsing within the API.
- The same change was made to the room Bookings api, so as to not force the roomId to be an int in the route.
    - The above 3 changes are made to ensure that any malformed or out of range Ids return 400 error instead of reaching the SPA fallback.
- Updated the Date parser helper methods to rely on date range between 2000-01-01 and 2099-12-31.
- Updated the FronendInteractionTest UserCanSubmitBookingAndSeeConfirmation to assert on name, date, startTime and endTime.
- Updated the api(url, options) function in app.js, to enhance the error handling, so that any network failures or error responses without a readable message use a user friendly message instead of the raw error.