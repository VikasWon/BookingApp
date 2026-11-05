using System.Text.Json.Serialization;

namespace BookingApp.Api.Models;

public sealed record CreateBookingRequest(int RoomId, string? Name, string? Date, string? StartTime, string? EndTime);
public sealed record RoomDto(int Id, string Name);
public sealed record BookingDto(int Id, int RoomId, string RoomName, string Name, DateTime StartUtc, DateTime EndUtc, bool IsCancelled);
public sealed record ErrorResponse(string Message, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Dictionary<string, string[]>? Errors = null);
