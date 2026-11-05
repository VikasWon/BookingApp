namespace BookingApp.Api.Models;

public sealed class Room
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public ICollection<Booking> Bookings { get; set; } = new List<Booking>();
}
