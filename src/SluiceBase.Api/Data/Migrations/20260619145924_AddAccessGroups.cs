using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SluiceBase.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAccessGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "access_group",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_access_group", x => x.id);
                    table.ForeignKey(
                        name: "fk_access_group_user_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "access_group_database_role",
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
                    table.PrimaryKey("pk_access_group_database_role", x => x.id);
                    table.ForeignKey(
                        name: "fk_access_group_database_role_access_group_group_id",
                        column: x => x.group_id,
                        principalTable: "access_group",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_access_group_database_role_database_database_id",
                        column: x => x.database_id,
                        principalTable: "server_database",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_access_group_database_role_user_granted_by_id",
                        column: x => x.granted_by_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "access_group_member",
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
                    table.PrimaryKey("pk_access_group_member", x => x.id);
                    table.ForeignKey(
                        name: "fk_access_group_member_access_group_group_id",
                        column: x => x.group_id,
                        principalTable: "access_group",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_access_group_member_user_added_by_id",
                        column: x => x.added_by_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_access_group_member_user_user_id",
                        column: x => x.user_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "access_group_permission",
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
                    table.PrimaryKey("pk_access_group_permission", x => x.id);
                    table.ForeignKey(
                        name: "fk_access_group_permission_access_group_group_id",
                        column: x => x.group_id,
                        principalTable: "access_group",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_access_group_permission_user_granted_by_id",
                        column: x => x.granted_by_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_access_group_created_by_id",
                table: "access_group",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_access_group_name",
                table: "access_group",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_access_group_database_role_database_id",
                table: "access_group_database_role",
                column: "database_id");

            migrationBuilder.CreateIndex(
                name: "ix_access_group_database_role_granted_by_id",
                table: "access_group_database_role",
                column: "granted_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_access_group_database_role_group_id_permission_database_id",
                table: "access_group_database_role",
                columns: new[] { "group_id", "permission", "database_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_access_group_member_added_by_id",
                table: "access_group_member",
                column: "added_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_access_group_member_group_id_user_id",
                table: "access_group_member",
                columns: new[] { "group_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_access_group_member_user_id",
                table: "access_group_member",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_access_group_permission_granted_by_id",
                table: "access_group_permission",
                column: "granted_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_access_group_permission_group_id_permission",
                table: "access_group_permission",
                columns: new[] { "group_id", "permission" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "access_group_database_role");

            migrationBuilder.DropTable(
                name: "access_group_member");

            migrationBuilder.DropTable(
                name: "access_group_permission");

            migrationBuilder.DropTable(
                name: "access_group");
        }
    }
}
