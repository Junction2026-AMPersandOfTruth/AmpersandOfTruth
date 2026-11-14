using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AmpersandOfTruth.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class WebSpreadCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WebSpreadCache",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Request = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    ResponseJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebSpreadCache", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "WebSpreadCalls",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    At = table.Column<long>(type: "INTEGER", nullable: false),
                    Scope = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Outcome = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    FromCache = table.Column<bool>(type: "INTEGER", nullable: false),
                    Live = table.Column<bool>(type: "INTEGER", nullable: false),
                    Request = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    LatencyMs = table.Column<long>(type: "INTEGER", nullable: false),
                    Error = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebSpreadCalls", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WebSpreadCache_Kind",
                table: "WebSpreadCache",
                column: "Kind");

            migrationBuilder.CreateIndex(
                name: "IX_WebSpreadCalls_At",
                table: "WebSpreadCalls",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_WebSpreadCalls_Scope",
                table: "WebSpreadCalls",
                column: "Scope");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WebSpreadCache");

            migrationBuilder.DropTable(
                name: "WebSpreadCalls");
        }
    }
}
