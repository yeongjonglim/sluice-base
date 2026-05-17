using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SluiceBase.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUserDatabaseRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_database_role",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permission = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    database_id = table.Column<Guid>(type: "uuid", nullable: false),
                    granted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    granted_by_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_database_role", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_database_role_server_database_database_id",
                        column: x => x.database_id,
                        principalTable: "server_database",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_user_database_role_user_granted_by_id",
                        column: x => x.granted_by_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_user_database_role_user_user_id",
                        column: x => x.user_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Seed: grant existing users access to all current databases for their existing scopeable permissions
            migrationBuilder.Sql(@"
                INSERT INTO user_database_role (id, user_id, permission, database_id, granted_at, granted_by_id)
                SELECT gen_random_uuid(), up.user_id, up.permission, sd.id, NOW(), NULL
                FROM user_permission up
                CROSS JOIN server_database sd
                WHERE up.permission IN ('query:execute', 'query:audit', 'update:submit', 'update:approve', 'update:execute')
                  AND sd.deleted_at IS NULL
                ON CONFLICT DO NOTHING;
            ");

            // Remove scopeable permissions from user_permission — they are now managed in user_database_role
            migrationBuilder.Sql(@"
                DELETE FROM user_permission
                WHERE permission IN ('query:execute', 'query:audit', 'update:submit', 'update:approve', 'update:execute');
            ");

            migrationBuilder.CreateIndex(
                name: "ix_user_database_role_database_id",
                table: "user_database_role",
                column: "database_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_database_role_granted_by_id",
                table: "user_database_role",
                column: "granted_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_database_role_user_id_permission_database_id",
                table: "user_database_role",
                columns: new[] { "user_id", "permission", "database_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restore: copy distinct scopeable permissions back to user_permission
            migrationBuilder.Sql(@"
                INSERT INTO user_permission (id, user_id, permission, granted_at, granted_by_id)
                SELECT DISTINCT gen_random_uuid(), user_id, permission, MIN(granted_at), NULL
                FROM user_database_role
                GROUP BY user_id, permission
                ON CONFLICT DO NOTHING;
            ");

            migrationBuilder.DropTable(
                name: "user_database_role");
        }
    }
}
