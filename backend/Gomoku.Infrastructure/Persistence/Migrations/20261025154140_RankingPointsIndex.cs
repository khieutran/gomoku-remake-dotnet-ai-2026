using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gomoku.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RankingPointsIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_RankingPoints_Id",
                table: "AspNetUsers",
                columns: new[] { "RankingPoints", "Id" },
                descending: new bool[0],
                filter: "\"RankingGames\" > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_RankingPoints_Id",
                table: "AspNetUsers");
        }
    }
}
