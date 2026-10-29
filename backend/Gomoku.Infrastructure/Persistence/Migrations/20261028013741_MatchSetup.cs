using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gomoku.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MatchSetup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HumanStone",
                table: "Matches",
                type: "character varying(1)",
                maxLength: 1,
                nullable: false,
                defaultValue: "X");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Match_HumanStone",
                table: "Matches",
                sql: "\"HumanStone\" IN ('X', 'O')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Match_HumanStone",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "HumanStone",
                table: "Matches");
        }
    }
}
