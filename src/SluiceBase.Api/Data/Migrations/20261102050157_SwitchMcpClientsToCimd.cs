using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SluiceBase.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class SwitchMcpClientsToCimd : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mcp_oauth_client");

            migrationBuilder.AddColumn<bool>(
                name: "is_loopback_grant",
                table: "mcp_token",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_loopback_grant",
                table: "mcp_token");

            migrationBuilder.CreateTable(
                name: "mcp_oauth_client",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<string>(type: "text", nullable: false),
                    client_name = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    redirect_uris = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mcp_oauth_client", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_mcp_oauth_client_client_id",
                table: "mcp_oauth_client",
                column: "client_id",
                unique: true);
        }
    }
}
