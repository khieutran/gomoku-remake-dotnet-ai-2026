using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gomoku.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AiTurnJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiTurnJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    GameId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpectedRevision = table.Column<long>(type: "bigint", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    LeaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastErrorCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiTurnJobs", x => x.Id);
                    table.CheckConstraint("CK_AiTurnJob_Attempts", "\"Attempts\" >= 0");
                    table.CheckConstraint("CK_AiTurnJob_Revision", "\"ExpectedRevision\" >= 0");
                    table.ForeignKey(
                        name: "FK_AiTurnJobs_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AiTurnJobs_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiTurnJobs_CompletedAt_NextAttemptAt",
                table: "AiTurnJobs",
                columns: new[] { "CompletedAt", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AiTurnJobs_GameId_ExpectedRevision",
                table: "AiTurnJobs",
                columns: new[] { "GameId", "ExpectedRevision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiTurnJobs_MatchId",
                table: "AiTurnJobs",
                column: "MatchId");

            migrationBuilder.Sql("""
                UPDATE "Games"
                SET "AiVersion" = 'legal-v1'
                WHERE "State" = 'Active' AND "AiVersion" IS NULL;

                INSERT INTO "AiTurnJobs"
                    ("Id", "MatchId", "GameId", "ExpectedRevision", "Attempts",
                     "NextAttemptAt", "CreatedAt")
                SELECT gen_random_uuid(), match."Id", game."Id", match."Revision", 0,
                       NOW(), NOW()
                FROM "Matches" AS match
                JOIN "Games" AS game ON game."Id" = match."CurrentGameId"
                LEFT JOIN LATERAL (
                    SELECT move."Stone"
                    FROM "Moves" AS move
                    WHERE move."GameId" = game."Id"
                    ORDER BY move."Step" DESC
                    LIMIT 1
                ) AS last_move ON TRUE
                WHERE game."State" = 'Active'
                  AND (last_move."Stone" = 'X' OR
                       (last_move."Stone" IS NULL AND game."OpeningStone" = 'O'));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiTurnJobs");
        }
    }
}
