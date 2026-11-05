namespace BookingApp.Api.Models;

public sealed class Booking
{
    public int Id { get; set; }
    public int RoomId { get; set; }
    public required string Name { get; set; }
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public Room Room { get; set; } = null!;
}
