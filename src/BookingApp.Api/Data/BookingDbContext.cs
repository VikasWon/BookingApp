using BookingApp.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BookingApp.Api.Data;

public sealed class BookingDbContext(DbContextOptions<BookingDbContext> options) : DbContext(options)
{
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<Booking> Bookings => Set<Booking>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var utcConverter = new ValueConverter<DateTime, DateTime>(
            value => DateTime.SpecifyKind(value, DateTimeKind.Utc),
            value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
        var nullableUtcConverter = new ValueConverter<DateTime?, DateTime?>(
            value => value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null,
            value => value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null);
        modelBuilder.Entity<Room>(entity =>
        {
            entity.HasKey(room => room.Id);
            entity.Property(room => room.Name).HasMaxLength(80).IsRequired();
            entity.HasIndex(room => room.Name).IsUnique();
        });
        modelBuilder.Entity<Booking>(entity =>
        {
            entity.HasKey(booking => booking.Id);
            entity.Property(booking => booking.Name).HasMaxLength(100).IsRequired();
            entity.Property(booking => booking.StartUtc).IsRequired();
            entity.Property(booking => booking.EndUtc).IsRequired();
            entity.Property(booking => booking.StartUtc).HasConversion(utcConverter);
            entity.Property(booking => booking.EndUtc).HasConversion(utcConverter);
            entity.Property(booking => booking.CancelledAtUtc).HasConversion(nullableUtcConverter);
            entity.HasIndex(booking => new { booking.RoomId, booking.StartUtc, booking.EndUtc, booking.CancelledAtUtc });
            entity.HasOne(booking => booking.Room).WithMany(room => room.Bookings).HasForeignKey(booking => booking.RoomId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
