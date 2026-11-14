using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AmpersandOfTruth.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSavedInvestigations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SavedInvestigations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Topic = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    TopicSlug = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    OwnerKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PipelineVersion = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SnapshotJson = table.Column<string>(type: "TEXT", nullable: false),
                    IncidentCount = table.Column<int>(type: "INTEGER", nullable: false),
                    TierCountsJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavedInvestigations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PinnedIncidents",
                columns: table => new
                {
                    InvestigationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IncidentId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    PinnedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PinnedIncidents", x => new { x.InvestigationId, x.IncidentId });
                    table.ForeignKey(
                        name: "FK_PinnedIncidents_SavedInvestigations_InvestigationId",
                        column: x => x.InvestigationId,
                        principalTable: "SavedInvestigations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SavedInvestigations_OwnerKey",
                table: "SavedInvestigations",
                column: "OwnerKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PinnedIncidents");

            migrationBuilder.DropTable(
                name: "SavedInvestigations");
        }
    }
}
