using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SluiceBase.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMcpGrant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "grant_expires_at",
                table: "mcp_token",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<Guid>(
                name: "grant_id",
                table: "mcp_token",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "ix_mcp_token_user_id_grant_id",
                table: "mcp_token",
                columns: new[] { "user_id", "grant_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_mcp_token_user_id_grant_id",
                table: "mcp_token");

            migrationBuilder.DropColumn(
                name: "grant_expires_at",
                table: "mcp_token");

            migrationBuilder.DropColumn(
                name: "grant_id",
                table: "mcp_token");
        }
    }
}
