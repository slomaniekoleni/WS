using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ws.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class SalonCountryAndCover : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Country",
                table: "Salons",
                type: "TEXT",
                nullable: false,
                defaultValue: "BY");

            migrationBuilder.AddColumn<string>(
                name: "CoverImageUrl",
                table: "Salons",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Country",
                table: "Salons");

            migrationBuilder.DropColumn(
                name: "CoverImageUrl",
                table: "Salons");
        }
    }
}
