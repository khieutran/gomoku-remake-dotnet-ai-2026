using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gomoku.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RankingOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RankingGames",
                table: "AspNetUsers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RankingPoints",
                table: "AspNetUsers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "RankingOutbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GameId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Points = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RankingOutbox", x => x.Id);
                    table.CheckConstraint("CK_RankingOutbox_Points", "\"Points\" >= 0");
                    table.ForeignKey(
                        name: "FK_RankingOutbox_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RankingOutbox_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_User_Ranking",
                table: "AspNetUsers",
                sql: "\"RankingPoints\" >= 0 AND \"RankingGames\" >= 0");

            migrationBuilder.Sql("""
                UPDATE "AspNetUsers" AS account
                SET "RankingGames" = totals.games,
                    "RankingPoints" = totals.points
                FROM (
                    SELECT match."UserId" AS account_id,
                           COUNT(*)::integer AS games,
                           SUM(CASE game."RuleStatus"
                               WHEN 'XWon' THEN 3
                               WHEN 'Draw' THEN 1
                               ELSE 0 END)::integer AS points
                    FROM "Games" AS game
                    JOIN "Matches" AS match ON match."Id" = game."MatchId"
                    WHERE match."UserId" IS NOT NULL AND game."State" = 'Finished'
                    GROUP BY match."UserId"
                ) AS totals
                WHERE account."Id" = totals.account_id;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_RankingOutbox_GameId",
                table: "RankingOutbox",
                column: "GameId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RankingOutbox_PublishedAt_CreatedAt",
                table: "RankingOutbox",
                columns: new[] { "PublishedAt", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RankingOutbox_UserId",
                table: "RankingOutbox",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RankingOutbox");

            migrationBuilder.DropCheckConstraint(
                name: "CK_User_Ranking",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "RankingGames",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "RankingPoints",
                table: "AspNetUsers");
        }
    }
}
