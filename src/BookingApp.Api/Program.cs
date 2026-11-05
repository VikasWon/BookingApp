using System.Globalization;
using System.Text.RegularExpressions;
using BookingApp.Api.Data;
using BookingApp.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
var databasePath = builder.Configuration["DatabasePath"] ?? Path.Combine(builder.Environment.ContentRootPath, "booking-app.db");
builder.Services.AddDbContext<BookingDbContext>(options => options.UseSqlite($"Data Source={databasePath};Default Timeout=10"));
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
    db.Database.Migrate();
    if (!db.Rooms.Any())
    {
        db.Rooms.AddRange(new Room { Name = "Vikas" }, new Room { Name = "Aashima" }, new Room { Name = "Riva" });
        db.SaveChanges();
    }
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/rooms", async (BookingDbContext db) =>
    Results.Ok(await db.Rooms.AsNoTracking().OrderBy(r => r.Id).Select(r => new RoomDto(r.Id, r.Name)).ToListAsync()));

app.MapGet("/api/bookings", async (string? date, BookingDbContext db) =>
{
    var errors = new Dictionary<string, string[]>();

    var dateError = DateError(date, out var day);
    if (dateError is not null)
        errors["date"] = [dateError];

    if (errors.Count > 0)
        return Validation(errors);

    var from = Utc(day, TimeOnly.MinValue);
    var until = from.AddDays(1);

    var results = await db.Bookings
        .AsNoTracking()
        .Where(b =>
            b.CancelledAtUtc == null &&
            b.StartUtc < until &&
            b.EndUtc > from)
        .OrderBy(b => b.RoomId)
        .ThenBy(b => b.StartUtc)
        .Select(b => new BookingDto(
            b.Id,
            b.RoomId,
            b.Room.Name,
            b.Name,
            b.StartUtc,
            b.EndUtc,
            false))
        .ToListAsync();

    return Results.Ok(results);
});

app.MapGet(
    "/api/rooms/{roomIdText}/bookings",
    async (string roomIdText, string? date, BookingDbContext db) =>
    {
        var errors = new Dictionary<string, string[]>();

        if (!TryParsePositiveId(roomIdText, out var roomId))
        {
            errors["roomId"] = [
                "Room ID must be a whole number greater than 0."
            ];
        }

        var dateError = DateError(date, out var day);
        if (dateError is not null)
            errors["date"] = [dateError];

        if (errors.Count > 0)
            return Validation(errors);

        if (!await db.Rooms.AnyAsync(r => r.Id == roomId))
            return NotFound("Room not found.");

        var from = Utc(day, TimeOnly.MinValue);
        var until = from.AddDays(1);

        var results = await db.Bookings
            .AsNoTracking()
            .Where(b =>
                b.RoomId == roomId &&
                b.CancelledAtUtc == null &&
                b.StartUtc < until &&
                b.EndUtc > from)
            .OrderBy(b => b.StartUtc)
            .Select(b => new BookingDto(
                b.Id,
                b.RoomId,
                b.Room.Name,
                b.Name,
                b.StartUtc,
                b.EndUtc,
                false))
            .ToListAsync();

        return Results.Ok(results);
    });

app.MapPost("/api/bookings", async (HttpRequest httpRequest, BookingDbContext db) =>
{
    JsonDocument body;

    try
    {
        body = await JsonDocument.ParseAsync(httpRequest.Body);
    }
    catch (JsonException)
    {
        return InvalidBody();
    }

    using (body)
    {
        var root = body.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
            return InvalidBody();

        var errors = new Dictionary<string, string[]>();

        if (!TryReadPositiveInt(root, "roomId", out var roomId))
        {
            errors["roomId"] = [
                "Room ID must be a whole number greater than 0."
            ];
        }

        var name = ReadJsonString(root, "name")?.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            errors["name"] = ["Name is required."];
        }
        else if (name.Length > 100)
        {
            errors["name"] = ["Name must be 100 characters or fewer."];
        }

        var dateText = ReadJsonString(root, "date");
        var date = default(DateOnly);
        var dateError = DateError(dateText, out date);

        if (dateError is not null)
            errors["date"] = [dateError];

        var startTimeText = ReadJsonString(root, "startTime");
        var endTimeText = ReadJsonString(root, "endTime");

        var startTimeOk = TryParseTime(startTimeText, out var startTime);
        var endTimeOk = TryParseTime(endTimeText, out var endTime);

        if (!startTimeOk)
            errors["startTime"] = ["Time must be in HH:mm format."];

        if (!endTimeOk)
            errors["endTime"] = ["Time must be in HH:mm format."];

        if (errors.Count > 0)
            return Validation(errors);

        var start = Utc(date, startTime);
        var end = Utc(date, endTime);

        // 00:00 as an end time means midnight at the end of the selected day.
        if (endTime == TimeOnly.MinValue && startTime > TimeOnly.MinValue)
            end = end.AddDays(1);

        if (end <= start)
        {
            return Validation(new Dictionary<string, string[]>
            {
                ["endTime"] = ["End time must be after start time."]
            });
        }

        // Check the room only after validating all request fields.
        if (!await db.Rooms.AnyAsync(room => room.Id == roomId))
            return NotFound("Room not found.");

        await using var transaction =
            await db.Database.BeginTransactionAsync();

        var conflict = await db.Bookings.AnyAsync(booking =>
            booking.RoomId == roomId &&
            booking.CancelledAtUtc == null &&
            booking.StartUtc < end &&
            booking.EndUtc > start);

        if (conflict)
        {
            await transaction.RollbackAsync();
            return Conflict(
                "That room is already booked for all or part of this time slot.");
        }

        var booking = new Booking
        {
            RoomId = roomId,
            Name = name!,
            StartUtc = start,
            EndUtc = end
        };

        db.Bookings.Add(booking);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        var room = await db.Rooms.FindAsync(roomId);

        return Results.Json(
            new BookingDto(
                booking.Id,
                booking.RoomId,
                room!.Name,
                booking.Name,
                booking.StartUtc,
                booking.EndUtc,
                false),
            statusCode: StatusCodes.Status201Created);
    }
});

app.MapDelete("/api/bookings/{bookingIdText}", async (string bookingIdText, BookingDbContext db) =>
{
    if (!TryParsePositiveId(bookingIdText, out var bookingId))
        return Validation(new Dictionary<string, string[]>
        {
            ["bookingId"] =
                ["Booking ID must be a whole number greater than 0."]
        });

    var booking = await db.Bookings.FirstOrDefaultAsync(b => b.Id == bookingId && b.CancelledAtUtc == null);
    if (booking is null) return NotFound("No active booking found with that ID.");
    booking.CancelledAtUtc = DateTime.UtcNow;
    await db.SaveChangesAsync();
    return Results.Ok(new BookingDto(booking.Id, booking.RoomId, (await db.Rooms.FindAsync(booking.RoomId))!.Name, booking.Name, booking.StartUtc, booking.EndUtc, true));
});

app.MapMethods(
    "/api/{**path}",
    ["GET", "HEAD", "POST", "PUT", "PATCH", "DELETE", "OPTIONS"],
    () => NotFound("Endpoint not found."));

app.MapFallbackToFile("index.html");
app.Run();

static DateTime Utc(DateOnly day, TimeOnly time) => DateTime.SpecifyKind(day.ToDateTime(time), DateTimeKind.Utc);

static string? DateError(string? value, out DateOnly date)
{
    date = default;

    if (!DateOnly.TryParseExact(
            value,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date))
    {
        return "Date must be a valid date in YYYY-MM-DD format.";
    }

    if (date < new DateOnly(2000, 1, 1) ||
        date > new DateOnly(2099, 12, 31))
    {
        return "Date must be between 2000-01-01 and 2099-12-31.";
    }

    return null;
}

static bool TryParseTime(string? value, out TimeOnly time)
{
    time = default;
    return !string.IsNullOrWhiteSpace(value) && Regex.IsMatch(value, "^(?:[01]\\d|2[0-3]):[0-5]\\d$") &&
        TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
}

static bool TryParsePositiveId(string? value, out int id) =>
    int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out id)
    && id > 0;

static bool TryReadPositiveInt(JsonElement root, string key, out int value)
{
    value = 0;

    return root.TryGetProperty(key, out var property) &&
           property.ValueKind == JsonValueKind.Number &&
           property.TryGetInt32(out value) &&
           value > 0;
}

static string? ReadJsonString(JsonElement root, string key) =>
    root.TryGetProperty(key, out var property) &&
    property.ValueKind == JsonValueKind.String
        ? property.GetString()
        : null;

static IResult InvalidBody() =>
    Results.Json(
        new ErrorResponse("Request body must be valid JSON."),
        statusCode: StatusCodes.Status400BadRequest);

static IResult Validation(Dictionary<string, string[]> errors) => Results.Json(new ErrorResponse("Please correct the highlighted fields.", errors), statusCode: StatusCodes.Status400BadRequest);
static IResult NotFound(string message) => Results.Json(new ErrorResponse(message), statusCode: StatusCodes.Status404NotFound);
static IResult Conflict(string message) => Results.Json(new ErrorResponse(message), statusCode: StatusCodes.Status409Conflict);

public partial class Program;
