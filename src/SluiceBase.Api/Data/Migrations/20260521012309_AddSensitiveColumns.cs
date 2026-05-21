using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SluiceBase.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSensitiveColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sensitive_column",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    database_id = table.Column<Guid>(type: "uuid", nullable: false),
                    schema_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    table_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    column_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    marked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    marked_by_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sensitive_column", x => x.id);
                    table.ForeignKey(
                        name: "fk_sensitive_column_server_database_database_id",
                        column: x => x.database_id,
                        principalTable: "server_database",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sensitive_column_user_marked_by_id",
                        column: x => x.marked_by_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "user_column_bypass",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sensitive_column_id = table.Column<Guid>(type: "uuid", nullable: false),
                    granted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    granted_by_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_column_bypass", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_column_bypass_sensitive_column_sensitive_column_id",
                        column: x => x.sensitive_column_id,
                        principalTable: "sensitive_column",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_user_column_bypass_user_granted_by_id",
                        column: x => x.granted_by_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_user_column_bypass_user_user_id",
                        column: x => x.user_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sensitive_column_database_id_schema_name_table_name_column_",
                table: "sensitive_column",
                columns: new[] { "database_id", "schema_name", "table_name", "column_name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sensitive_column_marked_by_id",
                table: "sensitive_column",
                column: "marked_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_column_bypass_granted_by_id",
                table: "user_column_bypass",
                column: "granted_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_column_bypass_sensitive_column_id",
                table: "user_column_bypass",
                column: "sensitive_column_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_column_bypass_user_id_sensitive_column_id",
                table: "user_column_bypass",
                columns: new[] { "user_id", "sensitive_column_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_column_bypass");

            migrationBuilder.DropTable(
                name: "sensitive_column");
        }
    }
}
