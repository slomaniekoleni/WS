using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ws.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class Reschedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ClientChangeNoticeHours",
                table: "Salons",
                type: "INTEGER",
                nullable: false,
                defaultValue: 24);

            migrationBuilder.AddColumn<DateTime>(
                name: "RescheduledFromUtc",
                table: "Bookings",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClientChangeNoticeHours",
                table: "Salons");

            migrationBuilder.DropColumn(
                name: "RescheduledFromUtc",
                table: "Bookings");
        }
    }
}
