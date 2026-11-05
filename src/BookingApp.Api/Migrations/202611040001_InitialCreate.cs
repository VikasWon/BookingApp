using BookingApp.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BookingApp.Api.Migrations;

[DbContext(typeof(BookingDbContext))]
[Migration("202611040001_InitialCreate")]
public sealed class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Rooms",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
                Name = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_Rooms", x => x.Id));

        migrationBuilder.CreateTable(
            name: "Bookings",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
                RoomId = table.Column<int>(type: "INTEGER", nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                StartUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                EndUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                CancelledAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Bookings", x => x.Id);
                table.ForeignKey("FK_Bookings_Rooms_RoomId", x => x.RoomId, "Rooms", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex("IX_Bookings_RoomId_StartUtc_EndUtc_CancelledAtUtc", "Bookings", new[] { "RoomId", "StartUtc", "EndUtc", "CancelledAtUtc" });
        migrationBuilder.CreateIndex("IX_Rooms_Name", "Rooms", "Name", unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("Bookings");
        migrationBuilder.DropTable("Rooms");
    }
}
