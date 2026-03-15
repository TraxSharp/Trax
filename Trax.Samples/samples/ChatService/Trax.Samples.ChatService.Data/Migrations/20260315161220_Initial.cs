using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trax.Samples.ChatService.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(name: "chat");

            migrationBuilder.CreateTable(
                name: "ChatRooms",
                schema: "chat",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    CreatedByUserId = table.Column<string>(type: "text", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatRooms", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "ChatMessages",
                schema: "chat",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChatRoomId = table.Column<Guid>(type: "uuid", nullable: false),
                    SenderUserId = table.Column<string>(type: "text", nullable: false),
                    SenderDisplayName = table.Column<string>(type: "text", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    SentAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatMessages_ChatRooms_ChatRoomId",
                        column: x => x.ChatRoomId,
                        principalSchema: "chat",
                        principalTable: "ChatRooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "ChatParticipants",
                schema: "chat",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChatRoomId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    DisplayName = table.Column<string>(type: "text", nullable: false),
                    JoinedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    LastReadAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatParticipants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatParticipants_ChatRooms_ChatRoomId",
                        column: x => x.ChatRoomId,
                        principalSchema: "chat",
                        principalTable: "ChatRooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_ChatRoomId_SentAt",
                schema: "chat",
                table: "ChatMessages",
                columns: new[] { "ChatRoomId", "SentAt" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_ChatParticipants_ChatRoomId_UserId",
                schema: "chat",
                table: "ChatParticipants",
                columns: new[] { "ChatRoomId", "UserId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_ChatParticipants_UserId",
                schema: "chat",
                table: "ChatParticipants",
                column: "UserId"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ChatMessages", schema: "chat");

            migrationBuilder.DropTable(name: "ChatParticipants", schema: "chat");

            migrationBuilder.DropTable(name: "ChatRooms", schema: "chat");
        }
    }
}
