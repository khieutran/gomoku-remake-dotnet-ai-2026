using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gomoku.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ActiveMatchCreatedCursorIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Matches_GuestSessionId_CreatedAt",
                table: "Matches");

            migrationBuilder.DropIndex(
                name: "IX_Matches_UserId_CreatedAt",
                table: "Matches");

            migrationBuilder.CreateIndex(
                name: "IX_Matches_GuestSessionId_CreatedAt_Id",
                table: "Matches",
                columns: new[] { "GuestSessionId", "CreatedAt", "Id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_Matches_UserId_CreatedAt_Id",
                table: "Matches",
                columns: new[] { "UserId", "CreatedAt", "Id" },
                descending: new[] { false, true, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Matches_GuestSessionId_CreatedAt_Id",
                table: "Matches");

            migrationBuilder.DropIndex(
                name: "IX_Matches_UserId_CreatedAt_Id",
                table: "Matches");

            migrationBuilder.CreateIndex(
                name: "IX_Matches_GuestSessionId_CreatedAt",
                table: "Matches",
                columns: new[] { "GuestSessionId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Matches_UserId_CreatedAt",
                table: "Matches",
                columns: new[] { "UserId", "CreatedAt" });
        }
    }
}
