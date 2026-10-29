using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Gomoku.Infrastructure.Persistence.Migrations;

[DbContext(typeof(GomokuDbContext))]
[Migration("20261028090000_BoardColumns")]
public partial class BoardColumns : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "BoardColumns", table: "Matches", type: "integer", nullable: true);
        migrationBuilder.AddCheckConstraint(
            name: "CK_Match_BoardColumns", table: "Matches",
            sql: "\"BoardColumns\" IS NULL OR \"BoardColumns\" > 0");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(name: "CK_Match_BoardColumns", table: "Matches");
        migrationBuilder.DropColumn(name: "BoardColumns", table: "Matches");
    }
}
