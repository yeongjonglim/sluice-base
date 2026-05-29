using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SluiceBase.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "group",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_group", x => x.id);
                    table.ForeignKey(
                        name: "fk_group_user_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "group_column_bypass",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sensitive_column_id = table.Column<Guid>(type: "uuid", nullable: false),
                    granted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    granted_by_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_group_column_bypass", x => x.id);
                    table.ForeignKey(
                        name: "fk_group_column_bypass_group_group_id",
                        column: x => x.group_id,
                        principalTable: "group",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_group_column_bypass_sensitive_column_sensitive_column_id",
                        column: x => x.sensitive_column_id,
                        principalTable: "sensitive_column",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_group_column_bypass_user_granted_by_id",
                        column: x => x.granted_by_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "group_database_role",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permission = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    database_id = table.Column<Guid>(type: "uuid", nullable: false),
                    granted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    granted_by_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_group_database_role", x => x.id);
                    table.ForeignKey(
                        name: "fk_group_database_role_group_group_id",
                        column: x => x.group_id,
                        principalTable: "group",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_group_database_role_server_database_database_id",
                        column: x => x.database_id,
                        principalTable: "server_database",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_group_database_role_user_granted_by_id",
                        column: x => x.granted_by_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "group_member",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    added_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    added_by_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_group_member", x => x.id);
                    table.ForeignKey(
                        name: "fk_group_member_group_group_id",
                        column: x => x.group_id,
                        principalTable: "group",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_group_member_user_added_by_id",
                        column: x => x.added_by_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_group_member_user_user_id",
                        column: x => x.user_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "group_permission_map",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permission = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    granted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    granted_by_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_group_permission_map", x => x.id);
                    table.ForeignKey(
                        name: "fk_group_permission_map_group_group_id",
                        column: x => x.group_id,
                        principalTable: "group",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_group_permission_map_user_granted_by_id",
                        column: x => x.granted_by_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_group_created_by_id",
                table: "group",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_group_name",
                table: "group",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_group_column_bypass_granted_by_id",
                table: "group_column_bypass",
                column: "granted_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_group_column_bypass_group_id_sensitive_column_id",
                table: "group_column_bypass",
                columns: new[] { "group_id", "sensitive_column_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_group_column_bypass_sensitive_column_id",
                table: "group_column_bypass",
                column: "sensitive_column_id");

            migrationBuilder.CreateIndex(
                name: "ix_group_database_role_database_id",
                table: "group_database_role",
                column: "database_id");

            migrationBuilder.CreateIndex(
                name: "ix_group_database_role_granted_by_id",
                table: "group_database_role",
                column: "granted_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_group_database_role_group_id_permission_database_id",
                table: "group_database_role",
                columns: new[] { "group_id", "permission", "database_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_group_member_added_by_id",
                table: "group_member",
                column: "added_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_group_member_group_id_user_id",
                table: "group_member",
                columns: new[] { "group_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_group_member_user_id",
                table: "group_member",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_group_permission_map_granted_by_id",
                table: "group_permission_map",
                column: "granted_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_group_permission_map_group_id_permission",
                table: "group_permission_map",
                columns: new[] { "group_id", "permission" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "group_column_bypass");

            migrationBuilder.DropTable(
                name: "group_database_role");

            migrationBuilder.DropTable(
                name: "group_member");

            migrationBuilder.DropTable(
                name: "group_permission_map");

            migrationBuilder.DropTable(
                name: "group");
        }
    }
}
