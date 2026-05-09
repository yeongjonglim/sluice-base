using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SluiceBase.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUpdateRequest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "update_request",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    server_id = table.Column<Guid>(type: "uuid", nullable: true),
                    submitter_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sql_text = table.Column<string>(type: "text", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    reviewer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    review_note = table.Column<string>(type: "text", nullable: true),
                    cancelled_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cancel_note = table.Column<string>(type: "text", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    executor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    executed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    exec_success = table.Column<bool>(type: "boolean", nullable: true),
                    exec_duration_ms = table.Column<int>(type: "integer", nullable: true),
                    exec_affected_rows = table.Column<int>(type: "integer", nullable: true),
                    exec_error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_update_request", x => x.id);
                    table.ForeignKey(
                        name: "fk_update_request_server_server_id",
                        column: x => x.server_id,
                        principalTable: "server",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_update_request_user_cancelled_by_id",
                        column: x => x.cancelled_by_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_update_request_user_executor_id",
                        column: x => x.executor_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_update_request_user_reviewer_id",
                        column: x => x.reviewer_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_update_request_user_submitter_id",
                        column: x => x.submitter_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_update_request_cancelled_by_id",
                table: "update_request",
                column: "cancelled_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_update_request_executor_id",
                table: "update_request",
                column: "executor_id");

            migrationBuilder.CreateIndex(
                name: "ix_update_request_reviewer_id",
                table: "update_request",
                column: "reviewer_id");

            migrationBuilder.CreateIndex(
                name: "ix_update_request_server_id",
                table: "update_request",
                column: "server_id");

            migrationBuilder.CreateIndex(
                name: "ix_update_request_submitter_id",
                table: "update_request",
                column: "submitter_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "update_request");
        }
    }
}
