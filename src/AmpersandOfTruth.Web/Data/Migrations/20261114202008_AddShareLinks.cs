using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AmpersandOfTruth.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddShareLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ShareLinks",
                columns: table => new
                {
                    TokenHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    InvestigationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    ExpiresAt = table.Column<long>(type: "INTEGER", nullable: true),
                    RevokedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShareLinks", x => x.TokenHash);
                    table.ForeignKey(
                        name: "FK_ShareLinks_SavedInvestigations_InvestigationId",
                        column: x => x.InvestigationId,
                        principalTable: "SavedInvestigations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ShareLinks_InvestigationId",
                table: "ShareLinks",
                column: "InvestigationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShareLinks");
        }
    }
}
