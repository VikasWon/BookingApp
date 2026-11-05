using System.Net;
using System.Net.Http.Json;
using BookingApp.Api.Data;
using BookingApp.Api.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace BookingApp.Tests;

public sealed class BookingApiTests : IAsyncLifetime
{
    private readonly TestApplication _app = new();
    private HttpClient _client = null!;
    public Task InitializeAsync() { _client = _app.CreateClient(); return Task.CompletedTask; }
    public async Task DisposeAsync() { _client.Dispose(); await _app.DisposeAsync(); _app.DeleteDatabase(); }

    [Fact]
    public async Task RoomsAndBothBookingQueriesReturnSeededRoomData()
    {
        var rooms = await _client.GetFromJsonAsync<RoomDto[]>("/api/rooms");
        Assert.NotNull(rooms); Assert.Equal(3, rooms.Length);
        Assert.Equal(HttpStatusCode.Created, (await Create("10:00", "11:00")).StatusCode);
        var all = await _client.GetFromJsonAsync<BookingDto[]>("/api/bookings?date=2026-11-04");
        var oneRoom = await _client.GetFromJsonAsync<BookingDto[]>($"/api/rooms/{rooms[0].Id}/bookings?date=2026-11-04");
        Assert.Single(all!); Assert.Single(oneRoom!);
    }

    [Fact]
    public async Task SwaggerUiAndOpenApiDocumentAreAvailableInDevelopment()
    {
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/swagger")).StatusCode);
        var document = await _client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, document.StatusCode);
        Assert.Contains("/api/bookings", await document.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task OverlappingBookingsConflictAndAdjacentBookingsPersist()
    {
        Assert.Equal(HttpStatusCode.Created, (await Create("10:00", "11:00")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Create("10:30", "11:30")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Create("11:00", "12:00")).StatusCode);
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        Assert.Equal(2, await db.Bookings.CountAsync(b => b.CancelledAtUtc == null));
        Assert.Equal(2, await db.Bookings.CountAsync());
    }

    [Theory]
    [InlineData("10:00:00", "11:00", "Time must be in HH:mm format.")]
    [InlineData("11:00", "10:00", "End time must be after start time.")]
    [InlineData("10:00", "10:00", "End time must be after start time.")]
    [InlineData("24:00", "23:59", "Time must be in HH:mm format.")]
    public async Task InvalidTimesReturnConsistentFieldErrors(string start, string end, string message)
    {
        var response = await Create(start, end);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.Equal("Please correct the highlighted fields.", error!.Message);
        Assert.Contains(error.Errors!.Values.SelectMany(x => x), value => value == message);
    }

    [Fact]
    public async Task NameAndDateValidationUseTheSharedErrorShape()
    {
        var blankName = await _client.PostAsJsonAsync("/api/bookings", new CreateBookingRequest(1, "   ", "2026-11-04", "10:00", "11:00"));
        Assert.Equal(HttpStatusCode.BadRequest, blankName.StatusCode);
        var blankError = await blankName.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.Equal("Name is required.", blankError!.Errors!["name"][0]);

        var longName = await _client.PostAsJsonAsync("/api/bookings", new CreateBookingRequest(1, new string('x', 101), "2026-11-04", "10:00", "11:00"));
        Assert.Equal(HttpStatusCode.BadRequest, longName.StatusCode);
        var longError = await longName.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.Equal("Name must be 100 characters or fewer.", longError!.Errors!["name"][0]);

        var badDate = await _client.GetAsync("/api/bookings?date=2026-02-30");
        Assert.Equal(HttpStatusCode.BadRequest, badDate.StatusCode);
        Assert.NotNull(await badDate.Content.ReadFromJsonAsync<ErrorResponse>());
        using var scope = _app.Services.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<BookingDbContext>().Bookings.ToListAsync());
    }

    [Fact]
    public async Task BookingCanEndExactlyAtNextUtcMidnight()
    {
        var response = await Create("23:00", "00:00");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var booking = await response.Content.ReadFromJsonAsync<BookingDto>();
        Assert.Equal(new DateTime(2026, 11, 5, 0, 0, 0, DateTimeKind.Utc), booking!.EndUtc);
        Assert.Contains("Z", (await response.Content.ReadAsStringAsync()));
        var previousDay = await _client.GetFromJsonAsync<BookingDto[]>("/api/bookings?date=2026-11-04");
        var nextDay = await _client.GetFromJsonAsync<BookingDto[]>("/api/bookings?date=2026-11-05");
        Assert.Single(previousDay!);
        Assert.Empty(nextDay!);
    }

    [Fact]
    public async Task UnknownRoomAndBookingReturnNotFound()
    {
        var roomBookings = await _client.GetAsync("/api/rooms/999/bookings?date=2026-11-04");
        Assert.Equal(HttpStatusCode.NotFound, roomBookings.StatusCode);
        Assert.NotNull(await roomBookings.Content.ReadFromJsonAsync<ErrorResponse>());
        var create = await Create("10:00", "11:00", 999);
        Assert.Equal(HttpStatusCode.NotFound, create.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.DeleteAsync("/api/bookings/999")).StatusCode);
    }

    [Fact]
    public async Task CancelledIntervalCanBeBookedAgainAndRepeatCancelIsNotFound()
    {
        var created = await Create("13:00", "14:00");
        var booking = await created.Content.ReadFromJsonAsync<BookingDto>();
        Assert.Equal(HttpStatusCode.OK, (await _client.DeleteAsync($"/api/bookings/{booking!.Id}")).StatusCode);
        Assert.Empty(await _client.GetFromJsonAsync<BookingDto[]>("/api/bookings?date=2026-11-04") ?? []);
        var secondCancel = await _client.DeleteAsync($"/api/bookings/{booking.Id}");
        Assert.Equal(HttpStatusCode.NotFound, secondCancel.StatusCode);
        Assert.Equal("No active booking found with that ID.", (await secondCancel.Content.ReadFromJsonAsync<ErrorResponse>())!.Message);
        Assert.Equal(HttpStatusCode.Created, (await Create("13:00", "14:00")).StatusCode);
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        Assert.Equal(2, await db.Bookings.CountAsync());
        Assert.Single(await db.Bookings.Where(b => b.CancelledAtUtc == null).ToListAsync());
    }

    [Fact]
    public async Task CompetingOverlappingRequestsOnlySaveOneBooking()
    {
        using var gate = new Barrier(2);
        async Task<HttpResponseMessage> Send() { gate.SignalAndWait(TimeSpan.FromSeconds(5)); return await Create("15:00", "16:00"); }
        var responses = await Task.WhenAll(Task.Run(Send), Task.Run(Send));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        Assert.Equal(1, await db.Bookings.CountAsync(b => b.CancelledAtUtc == null));
    }

    [Fact]
    public async Task DifferentRoomsCanBeBookedAtTheSameTime()
    {
        Assert.Equal(HttpStatusCode.Created, (await Create("09:00", "10:00", 1)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Create("09:00", "10:00", 2)).StatusCode);
    }

    private async Task<HttpResponseMessage> Create(string start, string end, int roomId = 1)
    {
        var response = await _client.PostAsJsonAsync("/api/bookings", new CreateBookingRequest(roomId, "Test Person", "2026-11-04", start, end));
        if (response.StatusCode == HttpStatusCode.InternalServerError)
            throw new InvalidOperationException(await response.Content.ReadAsStringAsync());
        return response;
    }

    private sealed class TestApplication : WebApplicationFactory<Program>
    {
        private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"booking-tests-{Guid.NewGuid():N}.db");
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("DatabasePath", _dbPath);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> { ["DatabasePath"] = _dbPath }));
            builder.ConfigureLogging(logging => logging.AddConsole().SetMinimumLevel(LogLevel.Error));
        }
        public void DeleteDatabase() { try { File.Delete(_dbPath); } catch { } }
    }
}
