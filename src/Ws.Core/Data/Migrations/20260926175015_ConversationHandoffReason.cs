using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ws.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class ConversationHandoffReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HandoffReason",
                table: "Conversations",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HandoffReason",
                table: "Conversations");
        }
    }
}
