using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AmpersandOfTruth.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class InvestigationCaches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EditReadingCache",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    PageId = table.Column<long>(type: "INTEGER", nullable: false),
                    RevertRevId = table.Column<long>(type: "INTEGER", nullable: false),
                    Json = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EditReadingCache", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "InvestigationRuns",
                columns: table => new
                {
                    Fingerprint = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Topic = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Json = table.Column<string>(type: "TEXT", nullable: false),
                    IncidentCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ElapsedMs = table.Column<double>(type: "REAL", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvestigationRuns", x => new { x.Fingerprint, x.Topic });
                });

            migrationBuilder.CreateIndex(
                name: "IX_EditReadingCache_PageId",
                table: "EditReadingCache",
                column: "PageId");

            migrationBuilder.CreateIndex(
                name: "IX_InvestigationRuns_CreatedAt",
                table: "InvestigationRuns",
                column: "CreatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EditReadingCache");

            migrationBuilder.DropTable(
                name: "InvestigationRuns");
        }
    }
}
