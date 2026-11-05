using BookingApp.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BookingApp.Api.Migrations;

[DbContext(typeof(BookingDbContext))]
public sealed class BookingDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "10.0.14").HasAnnotation("Relational:MaxIdentifierLength", 64);
        modelBuilder.Entity("BookingApp.Api.Models.Booking", b =>
        {
            b.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("INTEGER").HasAnnotation("Sqlite:Autoincrement", true);
            b.Property<DateTime?>("CancelledAtUtc").HasColumnType("TEXT");
            b.Property<DateTime>("EndUtc").HasColumnType("TEXT");
            b.Property<string>("Name").IsRequired().HasMaxLength(100).HasColumnType("TEXT");
            b.Property<int>("RoomId").HasColumnType("INTEGER");
            b.Property<DateTime>("StartUtc").HasColumnType("TEXT");
            b.HasKey("Id");
            b.HasIndex("RoomId", "StartUtc", "EndUtc", "CancelledAtUtc");
            b.ToTable("Bookings");
        });
        modelBuilder.Entity("BookingApp.Api.Models.Room", b =>
        {
            b.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("INTEGER").HasAnnotation("Sqlite:Autoincrement", true);
            b.Property<string>("Name").IsRequired().HasMaxLength(80).HasColumnType("TEXT");
            b.HasKey("Id");
            b.HasIndex("Name").IsUnique();
            b.ToTable("Rooms");
        });
        modelBuilder.Entity("BookingApp.Api.Models.Booking", b =>
        {
            b.HasOne("BookingApp.Api.Models.Room", "Room").WithMany("Bookings").HasForeignKey("RoomId").OnDelete(DeleteBehavior.Restrict).IsRequired();
            b.Navigation("Room");
        });
        modelBuilder.Entity("BookingApp.Api.Models.Room", b => b.Navigation("Bookings"));
    }
}
