using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SluiceBase.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddServer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "server",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    host = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    port = table.Column<int>(type: "integer", nullable: false),
                    database = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    read_username = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    encrypted_read_password = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    write_username = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    encrypted_write_password = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_server", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_server_name",
                table: "server",
                column: "name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "server");
        }
    }
}
