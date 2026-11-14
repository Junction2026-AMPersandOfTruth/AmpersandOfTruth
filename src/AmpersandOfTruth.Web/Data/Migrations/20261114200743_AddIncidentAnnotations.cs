using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AmpersandOfTruth.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIncidentAnnotations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IncidentAnnotations",
                columns: table => new
                {
                    InvestigationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IncidentId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Verdict = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    DismissReason = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    Source = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IncidentAnnotations", x => new { x.InvestigationId, x.IncidentId });
                    table.ForeignKey(
                        name: "FK_IncidentAnnotations_SavedInvestigations_InvestigationId",
                        column: x => x.InvestigationId,
                        principalTable: "SavedInvestigations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IncidentAnnotations");
        }
    }
}
