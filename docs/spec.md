# Meeting Room Booking App — Specification

## Purpose and scope

Build a local meeting-room booking web app. Its ASP.NET Core API owns the booking rules and persistence; the JavaScript frontend uses the API for all application actions. The app seeds three rooms, supports viewing bookings by selected date, creates bookings, and cancels bookings. No authentication, recurring bookings, notifications, or deployment are included.

## Time and booking rules

- All values are UTC. The API accepts no timezone suffix or offset; it interprets supplied date and time values as UTC, independently of the host timezone.
- Dates use `YYYY-MM-DD`. Times use 24-hour `HH:mm` with minute precision. Seconds, fractional seconds, and offset values are rejected with a field-level validation message, including `Time must be in HH:mm format.`
- The user name is required after trimming and is limited to 100 characters.
- Booking intervals are half-open: `[start, end)`. For the same room, overlap is `existing.start < requested.end && requested.start < existing.end`; adjacent bookings are allowed. Different rooms may be booked at the same time.
- A booking is limited to one UTC calendar day. `endTime: "00:00"` with a later start time means the following midnight, so a booking can end exactly at the selected day boundary. An equal start and end is invalid. A booking cannot start at the following midnight as part of the selected date.
- Cancelled bookings remain stored with a cancellation timestamp, are excluded from active booking results, and no longer block the time.

## API contract

All routes use `/api`; JSON properties are camel case.

| Method and route | Behavior |
| --- | --- |
| `GET /api/rooms` | List the three seeded rooms. |
| `GET /api/bookings?date=YYYY-MM-DD` | List active bookings across rooms for the UTC date. |
| `GET /api/rooms/{roomId}/bookings?date=YYYY-MM-DD` | List active bookings for one room on the UTC date. |
| `POST /api/bookings` | Create a booking. |
| `DELETE /api/bookings/{bookingId}` | Cancel an active booking by ID. |

Create request:

```json
{
  "roomId": 1,
  "name": "Alex Morgan",
  "date": "2026-11-04",
  "startTime": "10:00",
  "endTime": "11:00"
}
```

Creation returns `201 Created`; successful cancellation returns `200 OK`. Invalid input returns `400`; an unknown room, unknown booking ID, or already-cancelled booking returns `404`; a conflicting active booking returns `409`. Repeated cancellation returns `404` with `No active booking found with that ID.`

Every error has a `message`; field validation errors also include an `errors` object:

```json
{
  "message": "Please correct the highlighted fields.",
  "errors": { "startTime": ["Time must be in HH:mm format."] }
}
```

## Persistence and concurrency

Use a local SQLite file and source-controlled EF Core migrations. Apply migrations at startup and seed the three rooms idempotently. Booking creation must acquire an immediate SQLite write transaction before checking overlap, then insert within that transaction. SQLite serializes writes, so concurrent overlapping requests result in one `201`, one `409`, and one active row. Writes for different rooms are serialized too; that tradeoff is acceptable for a small local assessment app.

## Frontend

Provide a selected UTC date, a view of active bookings across rooms, room-specific viewing/filtering, and a booking form. The UI labels times as UTC, uses minute-level `HH:mm` controls, displays API validation and conflict messages, and lets users cancel bookings. It refreshes availability after a create or cancellation.

## Swagger and OpenAPI

- Enable Swagger UI in the Development environment at `/swagger` for interactive API exploration and ad-hoc requests.
- Expose the generated OpenAPI document at `/swagger/v1/swagger.json`.
- Include both endpoints in API integration test coverage.

## Acceptance tests

API integration tests cover listing rooms and bookings, create and cancellation actions, invalid dates and times, overlong or blank names, unknown rooms, invalid time order, overlap and adjacency with database assertions, same-time bookings in separate rooms, cancelled-slot rebooking, repeated cancellation, and concurrent competing requests with exactly one saved active booking. A frontend interaction test exercises booking submission and verifies the UI confirmation and API request payload.

The Swagger UI and generated OpenAPI document are reachable in Development.

## Local run and next improvements

Requires the .NET SDK version targeted by the repository. SQLite is supplied by the app's NuGet dependencies; no separate database server or SQLite install is needed. See the repository README for run, migration, and test commands.

A future higher-throughput or multi-instance version could move to PostgreSQL and enforce room-level non-overlap with an exclusion constraint. Structured logging and deployment-specific migration execution would also be useful next steps.
