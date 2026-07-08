using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SluiceBase.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMongoServerFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "auth_source",
                table: "server",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "connection_mode",
                table: "server",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "replica_set",
                table: "server",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "use_tls",
                table: "server",
                type: "boolean",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "auth_source",
                table: "server");

            migrationBuilder.DropColumn(
                name: "connection_mode",
                table: "server");

            migrationBuilder.DropColumn(
                name: "replica_set",
                table: "server");

            migrationBuilder.DropColumn(
                name: "use_tls",
                table: "server");
        }
    }
}
