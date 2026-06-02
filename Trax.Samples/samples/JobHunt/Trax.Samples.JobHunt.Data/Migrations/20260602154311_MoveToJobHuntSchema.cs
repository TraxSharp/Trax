using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trax.Samples.JobHunt.Data.Migrations
{
    /// <inheritdoc />
    public partial class MoveToJobHuntSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(name: "jobhunt");

            migrationBuilder.RenameTable(
                name: "WatchedCompanies",
                newName: "WatchedCompanies",
                newSchema: "jobhunt"
            );

            migrationBuilder.RenameTable(name: "Users", newName: "Users", newSchema: "jobhunt");

            migrationBuilder.RenameTable(
                name: "Profiles",
                newName: "Profiles",
                newSchema: "jobhunt"
            );

            migrationBuilder.RenameTable(
                name: "JobSnapshots",
                newName: "JobSnapshots",
                newSchema: "jobhunt"
            );

            migrationBuilder.RenameTable(name: "Jobs", newName: "Jobs", newSchema: "jobhunt");

            migrationBuilder.RenameTable(
                name: "EmailsSent",
                newName: "EmailsSent",
                newSchema: "jobhunt"
            );

            migrationBuilder.RenameTable(
                name: "EmailDrafts",
                newName: "EmailDrafts",
                newSchema: "jobhunt"
            );

            migrationBuilder.RenameTable(
                name: "Contacts",
                newName: "Contacts",
                newSchema: "jobhunt"
            );

            migrationBuilder.RenameTable(
                name: "Artifacts",
                newName: "Artifacts",
                newSchema: "jobhunt"
            );

            migrationBuilder.RenameTable(
                name: "Applications",
                newName: "Applications",
                newSchema: "jobhunt"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "WatchedCompanies",
                schema: "jobhunt",
                newName: "WatchedCompanies"
            );

            migrationBuilder.RenameTable(name: "Users", schema: "jobhunt", newName: "Users");

            migrationBuilder.RenameTable(name: "Profiles", schema: "jobhunt", newName: "Profiles");

            migrationBuilder.RenameTable(
                name: "JobSnapshots",
                schema: "jobhunt",
                newName: "JobSnapshots"
            );

            migrationBuilder.RenameTable(name: "Jobs", schema: "jobhunt", newName: "Jobs");

            migrationBuilder.RenameTable(
                name: "EmailsSent",
                schema: "jobhunt",
                newName: "EmailsSent"
            );

            migrationBuilder.RenameTable(
                name: "EmailDrafts",
                schema: "jobhunt",
                newName: "EmailDrafts"
            );

            migrationBuilder.RenameTable(name: "Contacts", schema: "jobhunt", newName: "Contacts");

            migrationBuilder.RenameTable(
                name: "Artifacts",
                schema: "jobhunt",
                newName: "Artifacts"
            );

            migrationBuilder.RenameTable(
                name: "Applications",
                schema: "jobhunt",
                newName: "Applications"
            );
        }
    }
}
