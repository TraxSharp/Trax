using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trax.Samples.JobHunt.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddJobSnapshotsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "JobSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    FetchedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    ContentHash = table.Column<string>(type: "text", nullable: false),
                    RawContent = table.Column<string>(type: "text", nullable: false),
                    DiffFromPrevious = table.Column<string>(type: "text", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobSnapshots", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_JobSnapshots_JobId_FetchedAt",
                table: "JobSnapshots",
                columns: new[] { "JobId", "FetchedAt" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "JobSnapshots");
        }
    }
}
