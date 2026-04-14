using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trax.Samples.JobHunt.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWatchedCompaniesTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WatchedCompanies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    CompanyName = table.Column<string>(type: "text", nullable: false),
                    CareersUrl = table.Column<string>(type: "text", nullable: false),
                    LastCheckedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    LastFingerprint = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WatchedCompanies", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_WatchedCompanies_UserId",
                table: "WatchedCompanies",
                column: "UserId"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "WatchedCompanies");
        }
    }
}
