using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AmpersandOfTruth.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AnalysisStore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AnalysisCache",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PromptId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    PromptVersion = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Model = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Outcome = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ResponseJson = table.Column<string>(type: "TEXT", nullable: true),
                    Error = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    InputTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    OutputTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    ThoughtTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnalysisCache", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "AnalysisCalls",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    At = table.Column<long>(type: "INTEGER", nullable: false),
                    Scope = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    PromptId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    PromptVersion = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Model = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Job = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Outcome = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    FromCache = table.Column<bool>(type: "INTEGER", nullable: false),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    InputTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    OutputTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    ThoughtTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    CachedTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    LatencyMs = table.Column<long>(type: "INTEGER", nullable: false),
                    EstimatedCostUsd = table.Column<double>(type: "REAL", nullable: false),
                    Error = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnalysisCalls", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnalysisCache_PromptId",
                table: "AnalysisCache",
                column: "PromptId");

            migrationBuilder.CreateIndex(
                name: "IX_AnalysisCalls_At",
                table: "AnalysisCalls",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_AnalysisCalls_PromptId",
                table: "AnalysisCalls",
                column: "PromptId");

            migrationBuilder.CreateIndex(
                name: "IX_AnalysisCalls_Scope",
                table: "AnalysisCalls",
                column: "Scope");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnalysisCache");

            migrationBuilder.DropTable(
                name: "AnalysisCalls");
        }
    }
}
